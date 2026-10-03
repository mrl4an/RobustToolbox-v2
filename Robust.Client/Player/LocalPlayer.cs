using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.ViewVariables;

namespace Robust.Client.Player
{
    /// <summary>
    ///     State and behaviour that belong to the local player's own session.
    /// </summary>
    public sealed class LocalPlayer
    {
        public LocalPlayer(ICommonSession session)
        {
            Session = session;
        }

        /// <summary>
        ///     The entity the session is currently attached to, or null when it controls nothing.
        ///     Read from the session on every access.
        /// </summary>
        [ViewVariables]
        public EntityUid? ControlledEntity => Session.AttachedEntity;

        [ViewVariables]
        public NetUserId UserId => Session.UserId;

        /// <summary>
        ///     The out-of-character name of the session, read live from it.
        /// </summary>
        [ViewVariables]
        public string Name => Session.Name;

        /// <summary>
        ///     The session of the local client.
        /// </summary>
        public ICommonSession Session;
    }
}
