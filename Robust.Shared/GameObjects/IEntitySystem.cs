using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    ///     Contract of a system that processes the components of one kind in bulk.
    ///     Entity systems are similar to TGstation13 subsystems.
    ///     They have a set of entities to run over and run every once in a while.
    ///     They get managed by an <see cref="IEntitySystemManager" />.
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
    [NotContentImplementable]
    public interface IEntitySystem : IEntityEventSubscriber
    {
        IEnumerable<Type> UpdatesAfter { get; }
        IEnumerable<Type> UpdatesBefore { get; }

        /// <summary>
        /// If prediction is disabled on the client, <see cref="Update"/> will not be ran unless this flag is set.
        /// </summary>
        bool UpdatesOutsidePrediction { get; }

        /// <summary>
        ///     Called once right after the system has been created. Use it to set up subscriptions and caches.
        /// </summary>
        void Initialize();

        /// <summary>
        ///     Called once before the system is destroyed. Use it to release resources and undo what
        ///     <see cref="Initialize"/> did.
        /// </summary>
        void Shutdown();

        /// <summary>
        ///     Called repeatedly to advance the system, once per frame or tick depending on the host.
        /// </summary>
        /// <param name="frameTime">Seconds elapsed since the previous call to this method.</param>
        void Update(float frameTime);
        void FrameUpdate(float frameTime);
    }
}
