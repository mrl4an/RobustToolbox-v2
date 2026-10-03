using System;
using Robust.Shared.Network;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    /// Manages the sending and receiving of network messages between the server and client(s).
    /// </summary>
    [NotContentImplementable]
    public interface IEntityNetworkManager
    {
        /// <summary>
        /// This event is raised when a system message comes in from the network.
        /// </summary>
        event EventHandler<object> ReceivedSystemMessage;

        /// <summary>
        /// Initializes networking for this manager. This should only be called once.
        /// </summary>
        void SetupNetworking();

        /// <summary>
        ///     Sends an entity-system message across the network.
        ///     On a client it goes to the server; on the server it is broadcast to every connected client.
        /// </summary>
        /// <param name="message">The event object to transmit to the matching entity systems.</param>
        /// <param name="recordReplay">Whether the message should also be written to a replay recording.</param>
        void SendSystemNetworkMessage(EntityEventArgs message, bool recordReplay = true);

        void SendSystemNetworkMessage(EntityEventArgs message, uint sequence)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Sends an entity-system message to one specific client. Only meaningful on the server;
        ///     client implementations throw <see cref="NotSupportedException"/>.
        /// </summary>
        /// <param name="message">The event object to transmit.</param>
        /// <param name="channel">The channel of the single client that should receive the message.</param>
        void SendSystemNetworkMessage(EntityEventArgs message, INetChannel channel);
    }
}
