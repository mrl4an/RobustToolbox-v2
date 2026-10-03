using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Robust.Shared.Input.Binding
{
    public delegate void StateInputCmdDelegate(ICommonSession? session);

    public abstract class InputCmdHandler
    {
        public virtual bool FireOutsidePrediction => false;

        public virtual void Enabled(ICommonSession? session)
        {
        }

        public virtual void Disabled(ICommonSession? session)
        {
        }

        public abstract bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message);

        /// <summary>
        ///     Creates a simple input command handler out of optional "enabled" and "disabled" delegates.
        /// </summary>
        /// <param name="enabled">Invoked when the bound key transitions to the down state. May be null.</param>
        /// <param name="disabled">Invoked when the bound key transitions to the up state. May be null.</param>
        /// <param name="handle">The value the handler reports as "message handled" after running a delegate.</param>
        /// <param name="outsidePrediction">Whether the handler also fires outside of prediction.</param>
        /// <returns>A new handler wrapping the given delegates.</returns>
        public static InputCmdHandler FromDelegate(StateInputCmdDelegate? enabled = null,
            StateInputCmdDelegate? disabled = null,
            bool handle = true,
            bool outsidePrediction = true)
        {
            return new StateInputCmdHandler
            {
                EnabledDelegate = enabled,
                DisabledDelegate = disabled,
                Handle = handle,
                OutsidePrediction = outsidePrediction
            };
        }

        private sealed class StateInputCmdHandler : InputCmdHandler
        {
            public StateInputCmdDelegate? EnabledDelegate;
            public StateInputCmdDelegate? DisabledDelegate;
            public bool Handle { get; set; }
            public bool OutsidePrediction;
            public override bool FireOutsidePrediction => OutsidePrediction;

            public override void Enabled(ICommonSession? session)
            {
                // A missing delegate simply means there is nothing to do on key down.
                EnabledDelegate?.Invoke(session);
            }

            public override void Disabled(ICommonSession? session)
            {
                // A missing delegate simply means there is nothing to do on key up.
                DisabledDelegate?.Invoke(session);
            }

            public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
            {
                switch (message.State)
                {
                    case BoundKeyState.Up:
                        Disabled(session);
                        return Handle;

                    case BoundKeyState.Down:
                        Enabled(session);
                        return Handle;

                    default:
                        // Unknown states can come from untrusted clients; ignore them.
                        return false;
                }
            }
        }
    }

    public delegate bool PointerInputCmdDelegate(ICommonSession? session, EntityCoordinates coords, EntityUid uid);

    public delegate bool PointerInputCmdDelegate2(in PointerInputCmdHandler.PointerInputCmdArgs args);

    public sealed class PointerInputCmdHandler : InputCmdHandler
    {
        private PointerInputCmdDelegate2 _callback;
        private bool _ignoreUp;

        public override bool FireOutsidePrediction { get; }

        /// <summary>
        /// Handler which will handle the command using the indicated callback
        /// </summary>
        /// <param name="callback">callback to handle the command</param>
        /// <param name="ignoreUp">whether keyup actions will be ignored by this handler (like lifting a key or releasing
        /// mouse button)</param>
        public PointerInputCmdHandler(PointerInputCmdDelegate callback, bool ignoreUp = true, bool outsidePrediction = false)
            : this((in PointerInputCmdArgs args) =>
            callback(args.Session, args.Coordinates, args.EntityUid), ignoreUp, outsidePrediction) { }

        /// <summary>
        ///     Handler that processes the command through the given callback.
        /// </summary>
        /// <param name="callback">Callback that receives the command arguments and reports whether it handled them.</param>
        /// <param name="ignoreUp">If true, key-up events (key lifted or mouse button released) are not forwarded
        /// to the callback.</param>
        /// <param name="outsidePrediction">Whether the handler also fires outside of prediction.</param>
        public PointerInputCmdHandler(PointerInputCmdDelegate2 callback, bool ignoreUp = true, bool outsidePrediction = false)
        {
            _callback = callback;
            _ignoreUp = ignoreUp;
            FireOutsidePrediction = outsidePrediction;
        }

        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            // Optionally skip everything that is not a key-down event.
            if (_ignoreUp && message.State != BoundKeyState.Down)
                return false;

            PointerInputCmdArgs args;

            switch (message)
            {
                case ClientFullInputCmdMessage clientMsg:
                    // Client messages already carry resolved coordinates and entity.
                    args = new PointerInputCmdArgs(session, clientMsg.Coordinates, clientMsg.ScreenCoordinates,
                        clientMsg.Uid, clientMsg.State, message);
                    break;

                case FullInputCmdMessage netMsg:
                    // Network messages carry networked ids that must be resolved first.
                    args = new PointerInputCmdArgs(session, entManager.GetCoordinates(netMsg.Coordinates),
                        netMsg.ScreenCoordinates, entManager.GetEntity(netMsg.Uid), netMsg.State, message);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            // With no callback nothing handled the message.
            return _callback != null && _callback(in args);
        }

        public readonly struct PointerInputCmdArgs
        {
            public readonly ICommonSession? Session;
            public readonly EntityCoordinates Coordinates;
            public readonly ScreenCoordinates ScreenCoordinates;
            public readonly EntityUid EntityUid;
            public readonly BoundKeyState State;
            public readonly IFullInputCmdMessage OriginalMessage;

            public PointerInputCmdArgs(ICommonSession? session, EntityCoordinates coordinates,
                ScreenCoordinates screenCoordinates, EntityUid entityUid, BoundKeyState state,
                IFullInputCmdMessage originalMessage)
            {
                Session = session;
                Coordinates = coordinates;
                ScreenCoordinates = screenCoordinates;
                EntityUid = entityUid;
                State = state;
                OriginalMessage = originalMessage;
            }
        }
    }

    public sealed class PointerStateInputCmdHandler : InputCmdHandler
    {
        private PointerInputCmdDelegate _enabled;
        private PointerInputCmdDelegate _disabled;
        public override bool FireOutsidePrediction { get; }

        public PointerStateInputCmdHandler(PointerInputCmdDelegate enabled, PointerInputCmdDelegate disabled, bool outsidePrediction = false)
        {
            _enabled = enabled;
            _disabled = disabled;
            FireOutsidePrediction = outsidePrediction;
        }

        /// <inheritdoc />
        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            switch (message)
            {
                case ClientFullInputCmdMessage clientMsg:
                    switch (clientMsg.State)
                    {
                        case BoundKeyState.Up:
                            return _disabled != null && _disabled(session, clientMsg.Coordinates, clientMsg.Uid);

                        case BoundKeyState.Down:
                            return _enabled != null && _enabled(session, clientMsg.Coordinates, clientMsg.Uid);
                    }
                    break;

                case FullInputCmdMessage netMsg:
                    switch (netMsg.State)
                    {
                        case BoundKeyState.Up:
                            return _disabled != null && _disabled(session,
                                entManager.GetCoordinates(netMsg.Coordinates), entManager.GetEntity(netMsg.Uid));

                        case BoundKeyState.Down:
                            return _enabled != null && _enabled(session,
                                entManager.GetCoordinates(netMsg.Coordinates), entManager.GetEntity(netMsg.Uid));
                    }
                    break;
            }

            // Unknown message types or key states are deliberately ignored.
            return false;
        }
    }

    /// <summary>
    /// Consumes both up and down states without calling any handler delegates. Primarily used on the client to
    /// prevent an input message from being sent to the server.
    /// </summary>
    public sealed class NullInputCmdHandler : InputCmdHandler
    {
        /// <inheritdoc />
        public override bool HandleCmdMessage(IEntityManager entManager, ICommonSession? session, IFullInputCmdMessage message)
        {
            return true;
        }
    }
}
