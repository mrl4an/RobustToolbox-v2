using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using System;
using System.Diagnostics;
using System.Linq;
using NetSerializer;
using Robust.Shared.Timing;

namespace Robust.Shared.GameStates
{
    [DebuggerDisplay("GameState from={FromSequence} to={ToSequence}")]
    [Serializable, NetSerializable]
    public sealed class GameState
    {
        /// <summary>
        ///     The serialized size in bytes of this game state.
        /// </summary>
        [field:NonSerialized]
        public int PayloadSize { get; set; }

        public bool ForceSendReliably;

        /// <summary>
        ///     Builds a game state from its parts. Nothing is validated or copied: the collection wrappers are
        ///     stored as given and therefore shared with the caller.
        /// </summary>
        /// <param name="fromSequence">Tick this state is a delta from.</param>
        /// <param name="toSequence">Tick this state brings the receiver up to.</param>
        /// <param name="lastInput">Last input command that was processed.</param>
        /// <param name="entities">Entity states contained in this game state.</param>
        /// <param name="players">Player/session states contained in this game state.</param>
        /// <param name="deletions">Entities that were deleted in this interval.</param>
        public GameState(GameTick fromSequence, GameTick toSequence, uint lastInput, NetListAsArray<EntityState> entities, NetListAsArray<SessionState> players, NetListAsArray<NetEntity> deletions)
        {
            FromSequence = fromSequence;
            ToSequence = toSequence;
            LastProcessedInput = lastInput;
            EntityStates = entities;
            PlayerStates = players;
            EntityDeletions = deletions;
        }

        public readonly GameTick FromSequence;
        public readonly GameTick ToSequence;

        public readonly uint LastProcessedInput;

        public readonly NetListAsArray<EntityState> EntityStates;
        public readonly NetListAsArray<SessionState> PlayerStates;
        public readonly NetListAsArray<NetEntity> EntityDeletions;

        /// <summary>
        /// Clone the game state's collections. Required for integration tests, to avoid the server/client referencing
        /// the same objects
        /// </summary>
        public GameState Clone()
        {
            // TODO integration test serialization.
            return new(
                FromSequence,
                ToSequence,
                LastProcessedInput,
                EntityStates.Value.ToArray(),
                PlayerStates.Value.Select(x=> x.Clone()).ToArray(),
                EntityDeletions.Value.ToArray());

        }
    }
}
