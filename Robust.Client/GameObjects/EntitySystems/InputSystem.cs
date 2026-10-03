using System;
using System.Numerics;
using Robust.Client.GameStates;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Client.GameObjects
{
    /// <summary>
    ///     Runs on the client and feeds the player's input commands into the simulation.
    /// </summary>
    public sealed class InputSystem : SharedInputSystem, IPostInjectInit
    {
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IClientGameStateManager _stateManager = default!;
        [Dependency] private readonly IConsoleHost _conHost = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly SharedTransformSystem _transform = default!;

        private ISawmill _sawmillInputContext = default!;

        private readonly IPlayerCommandStates _cmdStates = new PlayerCommandStates();

        /// <summary>
        ///     The held/released state of every key function, as last seen by this system.
        /// </summary>
        public IPlayerCommandStates CmdStates => _cmdStates;

        /// <summary>
        /// If the input system is currently predicting input.
        /// </summary>
        public bool Predicted { get; private set; }

        /// <summary>
        ///     Runs an input command through the local handlers and, if none of them consumes it,
        ///     forwards it to the server.
        /// </summary>
        /// <param name="session">The session issuing the command. On the client this is always the local player's session.</param>
        /// <param name="function">The key function whose state is changing.</param>
        /// <param name="message">The command data (tick, coordinates, state, target entity, ...).</param>
        /// <param name="replay">
        ///     If true, the stored key state is neither compared nor updated. Use this when re-playing an
        ///     old input that was saved or buffered.
        /// </param>
        /// <returns>
        ///     True if a local handler consumed the command (nothing is sent to the server). False if the
        ///     command was dropped as a repeat of the current state, or if it was sent to the server.
        /// </returns>
        public bool HandleInputCommand(ICommonSession? session, BoundKeyFunction function, IFullInputCmdMessage message, bool replay = false)
        {
#if DEBUG
            var expectedId = _inputManager.NetworkBindMap.KeyFunctionID(function);
            DebugTools.Assert(expectedId == message.InputFunctionId, "Function ID in the message does not match the function.");
#endif

            if (!replay)
            {
                // Ignore repeats of the state we already have.
                if (_cmdStates.GetState(function) == message.State)
                    return false;

                // The new state is recorded before any handler runs, whatever happens afterwards.
                _cmdStates.SetState(function, message.State);
            }

            // Give local handlers the first chance to consume the command.
            foreach (var handler in BindRegistry.GetHandlers(function))
            {
                if (!_stateManager.IsPredictionEnabled && !handler.FireOutsidePrediction)
                    continue;

                if (handler.HandleCmdMessage(EntityManager, session, message))
                    return true;
            }

            // Make sure we hold both the local and the networked representation of the command.
            ClientFullInputCmdMessage clientMsg;
            switch (message)
            {
                case ClientFullInputCmdMessage alreadyClient:
                    clientMsg = alreadyClient;
                    break;
                case FullInputCmdMessage net:
                    clientMsg = new ClientFullInputCmdMessage(
                        net.Tick,
                        net.SubTick,
                        net.InputFunctionId,
                        GetCoordinates(net.Coordinates),
                        net.ScreenCoordinates,
                        net.State,
                        GetEntity(net.Uid));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            FullInputCmdMessage fullMsg;
            switch (message)
            {
                case FullInputCmdMessage alreadyNet:
                    fullMsg = alreadyNet;
                    break;
                case ClientFullInputCmdMessage:
                    fullMsg = new FullInputCmdMessage(
                        clientMsg.Tick,
                        clientMsg.SubTick,
                        clientMsg.InputFunctionId,
                        clientMsg.State,
                        GetNetCoordinates(clientMsg.Coordinates),
                        clientMsg.ScreenCoordinates,
                        GetNetEntity(clientMsg.Uid));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            DispatchInputCommand(clientMsg, fullMsg);
            return false;
        }

        /// <summary>
        /// Handle a predicted input command.
        /// </summary>
        /// <param name="inputCmd">Input command to handle as predicted.</param>
        public void PredictInputCommand(IFullInputCmdMessage inputCmd)
        {
            var keyFunc = _inputManager.NetworkBindMap.KeyFunctionName(inputCmd.InputFunctionId);

            Predicted = true;
            var session = _playerManager.LocalSession;
            foreach (var handler in BindRegistry.GetHandlers(keyFunc))
            {
                if (handler.HandleCmdMessage(EntityManager, session, inputCmd))
                    break;
            }
            Predicted = false;

        }

        private void DispatchInputCommand(ClientFullInputCmdMessage clientMsg, FullInputCmdMessage message)
        {
            _stateManager.InputCommandDispatched(clientMsg, message);
            EntityManager.EntityNetManager?.SendSystemNetworkMessage(message, message.InputSequence);
        }

        public override void Initialize()
        {
            SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnAttachedEntityChanged);

            _conHost.RegisterCommand("incmd",
                "Inserts an input command into the simulation",
                "incmd <KeyFunction> <KeyState> [wxPos] [wyPos]",
                GenerateInputCommand);
        }

        public override void Shutdown()
        {
            base.Shutdown();

            _conHost.UnregisterCommand("incmd");
        }

        private void GenerateInputCommand(IConsoleShell shell, string argstr, string[] args)
        {
            if (_playerManager.LocalEntity is not { } pent)
                return;

            if (args.Length is not (2 or 4))
            {
                shell.WriteLine(Loc.GetString($"cmd-invalid-arg-number-error"));
                return;
            }

            var keyFunction = new BoundKeyFunction(args[0]);
            if (!Enum.TryParse<BoundKeyState>(args[1], out var state))
            {
                shell.WriteLine(Loc.GetString("cmd-parse-failure-enum", ("arg", args[1]), ("enum", nameof(BoundKeyState))));
                return;
            }

            var wOffset = Vector2.Zero;
            if (args.Length == 4)
            {
                if (!float.TryParse(args[2], out var wX))
                {
                    shell.WriteError(Loc.GetString("cmd-parse-failure-float", ("arg", args[2])));
                    return;
                }

                if (!float.TryParse(args[3], out var wY))
                {
                    shell.WriteError(Loc.GetString("cmd-parse-failure-float", ("arg", args[3])));
                    return;
                }

                wOffset = new Vector2(wX, wY);
            }

            var coords = _transform.ToCoordinates(pent, _transform.GetMapCoordinates(pent).Offset(wOffset));
            var funcId = _inputManager.NetworkBindMap.KeyFunctionID(keyFunction);

            var message = new ClientFullInputCmdMessage(_timing.CurTick,
                _timing.TickFraction,
                funcId,
                coords,
                new ScreenCoordinates(0, 0, default),
                state,
                EntityUid.Invalid);

            HandleInputCommand(_playerManager.LocalSession, keyFunction, message);
        }

        private void OnAttachedEntityChanged(LocalPlayerAttachedEvent message)
        {
            if (message.Entity != default)
            {
                SetEntityContextActive(_inputManager, message.Entity);
            }
            else
            {
                // Detached from everything: fall back to the default context.
                _inputManager.Contexts.SetActiveContext(InputContextContainer.DefaultContextName);
            }
        }

        private void SetEntityContextActive(IInputManager inputMan, EntityUid entity)
        {
            if (entity == default || !Exists(entity))
                throw new ArgumentNullException(nameof(entity));

            if (!TryComp(entity, out InputComponent? inputComp))
            {
                _sawmillInputContext.Debug(
                    $"Attached entity {entity} (prototype: {Comp<MetaDataComponent>(entity).EntityPrototype?.ID}) has no InputComponent, setting context to the default '{InputContextContainer.DefaultContextName}'.");
                inputMan.Contexts.SetActiveContext(InputContextContainer.DefaultContextName);
                return;
            }

            if (inputMan.Contexts.Exists(inputComp.ContextName))
            {
                inputMan.Contexts.SetActiveContext(inputComp.ContextName);
            }
            else
            {
                _sawmillInputContext.Error(
                    $"Attached entity {entity} (prototype: {Comp<MetaDataComponent>(entity).EntityPrototype?.ID}) has unknown input context '{inputComp.ContextName}', setting context to the default '{InputContextContainer.DefaultContextName}'.");
                inputMan.Contexts.SetActiveContext(InputContextContainer.DefaultContextName);
            }
        }

        /// <summary>
        ///     Activates the input context defined by the attached entity. Does nothing if no entity is attached.
        /// </summary>
        public void SetEntityContextActive()
        {
            if (_playerManager.LocalEntity is not { } entity)
                return;

            SetEntityContextActive(_inputManager, entity);
        }

        protected override void PostInject()
        {
            base.PostInject();

            _sawmillInputContext = _logManager.GetSawmill("input.context");
        }
    }
}
