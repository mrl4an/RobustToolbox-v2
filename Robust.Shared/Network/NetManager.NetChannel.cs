using System;
using System.Collections.Immutable;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lidgren.Network;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.Network
{
    public partial class NetManager
    {
        private sealed class NetChannel : INetChannel
        {
            private readonly NetManager _manager;
            private readonly NetConnection _connection;

            /// <inheritdoc />
            [ViewVariables]
            public long ConnectionId => _connection.RemoteUniqueIdentifier;

            /// <inheritdoc />
            [ViewVariables]
            public INetManager NetPeer => _manager;

            [ViewVariables] public string UserName => UserData.UserName;
            [ViewVariables] public LoginType AuthType { get; }
            [ViewVariables] public TimeSpan RemoteTimeOffset => TimeSpan.FromSeconds(_connection.RemoteTimeOffset);
            [ViewVariables] public TimeSpan RemoteTime => _manager._timing.RealTime + RemoteTimeOffset;

            /// <inheritdoc />
            [ViewVariables]
            public short Ping => (short) Math.Round(_connection.AverageRoundtripTime * 1000);

            /// <inheritdoc />
            [ViewVariables]
            public bool IsConnected => _connection.Status == NetConnectionStatus.Connected;

            /// <inheritdoc />
            public IPEndPoint RemoteEndPoint => _connection.RemoteEndPoint;

            /// <summary>
            ///     The underlying Lidgren connection. The manager uses it as the lookup key for this channel
            ///     and for raw Lidgren sends.
            /// </summary>
            public NetConnection Connection => _connection;

            [ViewVariables] public NetUserId UserId => UserData.UserId;
            [ViewVariables] public NetUserData UserData { get; }

            public bool IsHandshakeComplete { get; set; }

            // Only used on server, contains the encryption to use for this channel.
            public NetEncryption? Encryption { get; set; }

            [ViewVariables] public int CurrentMtu => _connection.CurrentMTU;

            public ChannelWriter<EncryptChannelItem>? EncryptionChannel;
            public Task? EncryptionChannelTask;

            /// <summary>
            ///     Creates a new instance of a <see cref="NetChannel"/>.
            /// </summary>
            /// <param name="manager">The net manager this channel belongs to.</param>
            /// <param name="connection">The raw Lidgren connection to the remote peer.</param>
            /// <param name="userData">Identity data of the remote user.</param>
            /// <param name="loginType">How the remote user authenticated.</param>
            internal NetChannel(NetManager manager, NetConnection connection, NetUserData userData, LoginType loginType)
            {
                _manager = manager;
                _connection = connection;
                UserData = userData;
                AuthType = loginType;
            }

            /// <inheritdoc />
            public T CreateNetMessage<T>() where T : NetMessage, new()
            {
                // The message is not tied to this channel; the manager builds it for the registered type.
                return _manager.CreateNetMessage<T>();
            }

            /// <inheritdoc />
            public void SendMessage(NetMessage message)
            {
                if (_manager.IsClient)
                {
                    // A client has a single remote peer (the server), so no recipient is needed.
                    _manager.ClientSendMessage(message);
                    return;
                }

                // A server must be told which client receives the message.
                _manager.ServerSendMessage(message, this);
            }

            /// <inheritdoc />
            public void Disconnect(string reason)
            {
                Disconnect(reason, true);
            }

            public void Disconnect(string reason, bool sendBye)
            {
                // Only an established connection can be disconnected; any other state is a no-op.
                if (_connection.Status != NetConnectionStatus.Connected)
                {
                    return;
                }

                // Completion is asynchronous and surfaces later through the manager's status-change handling.
                _connection.Disconnect(reason, sendBye);
            }

            public bool CanSendImmediately(NetDeliveryMethod method, int sequenceChannel)
            {
                return _connection.CanSendImmediately(method, sequenceChannel);
            }

            public override string ToString()
            {
                return $"{ConnectionId}/{UserId}";
            }
        }
    }
}
