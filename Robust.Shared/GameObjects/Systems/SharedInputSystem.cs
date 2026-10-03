using Robust.Shared.Input.Binding;

namespace Robust.Shared.GameObjects
{
    public abstract class SharedInputSystem : EntitySystem
    {
        private CommandBindRegistry _bindRegistry = default!;

        protected override void PostInject()
        {
            base.PostInject();

            _bindRegistry = new CommandBindRegistry(Log);
        }

        /// <summary>
        ///     Registry that maps key functions to their command handlers for this simulation.
        ///     Created once during injection.
        /// </summary>
        public ICommandBindRegistry BindRegistry => _bindRegistry;
    }
}
