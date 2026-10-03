using Robust.Shared.Serialization;
using System;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.ViewVariables;

#nullable disable

namespace Robust.Shared.GameStates
{
    [Serializable, NetSerializable]
    public sealed class SessionState
    {
        [ViewVariables]
        public NetUserId UserId { get; set; }

        /// <summary>
        ///     Display name of the player this state describes. Null until assigned.
        /// </summary>
        [ViewVariables]
        public string Name { get; set; }

        /// <summary>
        ///     Current connection status of the session.
        /// </summary>
        [ViewVariables]
        public SessionStatus Status { get; set; }

        // TODO PlayerManager
        // Network ping information, though probably do it outside of SessionState to avoid re-sending the name and such
        // for all players every few seconds.
        /// <summary>
        ///     Round-trip latency of the player. Not populated or networked at present, and not copied by
        ///     <see cref="Clone"/>.
        /// </summary>
        [Obsolete("Ping data is not currently networked")]
        [ViewVariables]
        public short Ping { get; set; }

        [ViewVariables]
        public NetEntity? ControlledEntity { get; set; }

        public SessionState Clone()
        {
            return new SessionState
            {
                UserId = UserId,
                Name = Name,
                Status = Status,
                ControlledEntity = ControlledEntity
            };
        }
    }
}
