using Robust.Shared.Player;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Errors;

namespace Robust.Server.Console
{
    internal sealed class ConGroupController : IConGroupController, IPermissionController
    {
        public IConGroupControllerImplementation? Implementation { get; set; }

        /// <summary>
        ///     Asks the backend whether the session may run the named command.
        /// </summary>
        /// <returns>The backend's answer, or false when no backend is assigned.</returns>
        public bool CanCommand(ICommonSession session, string cmdName)
        {
            if (Implementation is { } backend)
                return backend.CanCommand(session, cmdName);

            return false;
        }

        public bool CanAdminPlace(ICommonSession session)
        {
            return Implementation?.CanAdminPlace(session) ?? false;
        }

        public bool CanScript(ICommonSession session)
        {
            return Implementation?.CanScript(session) ?? false;
        }

        /// <summary>
        ///     Asks the backend whether the session may open the admin menu.
        /// </summary>
        /// <returns>The backend's answer, or false when no backend is assigned.</returns>
        public bool CanAdminMenu(ICommonSession session)
        {
            if (Implementation is { } backend)
                return backend.CanAdminMenu(session);

            return false;
        }

        public bool CanAdminReloadPrototypes(ICommonSession session)
        {
            return Implementation?.CanAdminReloadPrototypes(session) ?? false;
        }

        public bool CheckInvokable(CommandSpec command, ICommonSession? user, out IConError? error)
        {
            error = null;
            return Implementation?.CheckInvokable(command, user, out error) ?? false;
        }
    }
}
