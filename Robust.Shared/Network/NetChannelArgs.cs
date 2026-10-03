using System;
using System.Collections.Generic;
using System.Net;

namespace Robust.Shared.Network
{
    /// <summary>
    /// Data attached to events about a network channel.
    /// </summary>
    [Virtual]
    public class NetChannelArgs : EventArgs
    {
        /// <summary>
        ///     The network channel that raised the event. Assigned once by the constructor.
        /// </summary>
        public readonly INetChannel Channel;

        /// <summary>
        ///     Creates event arguments that refer to the given channel.
        /// </summary>
        /// <param name="channel">The channel that raised the event. It is stored as-is, without validation.</param>
        public NetChannelArgs(INetChannel channel)
        {
            Channel = channel;
        }
    }

    /// <summary>
    /// Data for the event that announces an incoming connection.
    /// </summary>
    public sealed class NetConnectingArgs : EventArgs
    {
        public bool IsDenied => DenyReasonData != null;

        public string? DenyReason => DenyReasonData?.Text;
        public NetDenyReason? DenyReasonData { get; private set; }

        public NetUserData UserData { get; }

        /// <summary>
        ///     The unique identifier of the user that is trying to connect.
        ///     Forwards to <see cref="NetUserData.UserId"/> of <see cref="UserData"/>.
        /// </summary>
        public NetUserId UserId => UserData.UserId;
        public string UserName => UserData.UserName;

        public IPEndPoint IP { get; }
        public LoginType AuthType { get; }

        public void Deny(string reason)
        {
            Deny(new NetDenyReason(reason));
        }

        public void Deny(NetDenyReason reason)
        {
            DenyReasonData = reason;
        }

        /// <summary>
        ///     Creates arguments describing an incoming connection attempt. The connection starts out not denied.
        /// </summary>
        /// <param name="data">Identity data of the connecting user.</param>
        /// <param name="ip">The remote endpoint the connection comes from.</param>
        /// <param name="authType">The login type the user authenticated with.</param>
        public NetConnectingArgs(NetUserData data, IPEndPoint ip, LoginType authType)
        {
            UserData = data;
            IP = ip;
            AuthType = authType;
        }
    }

    /// <summary>
    /// Contains a reason for denying a client connection to the game server.
    /// </summary>
    /// <param name="Text">The textual reason, presented to the user.</param>
    /// <param name="AdditionalProperties">
    /// Additional JSON properties that will be included in the <see cref="NetDisconnectMessage"/>.
    /// Valid value types are: string, int, float, bool.
    /// </param>
    /// <seealso cref="NetDisconnectMessage"/>
    /// <seealso cref="NetConnectingArgs"/>
    public record NetDenyReason(string Text, Dictionary<string, object> AdditionalProperties)
    {
        public NetDenyReason(string Text) : this(Text, new Dictionary<string, object>())
        {
        }
    }

    /// <summary>
    /// Structured reason common interface.
    /// </summary>
    [NotContentImplementable]
    public interface INetStructuredReason
    {
        NetDisconnectMessage Message { get; }
        string Reason { get; }
        bool RedialFlag { get; }
    }

    /// <summary>
    /// Data describing a connection attempt that failed.
    /// </summary>
    public sealed class NetConnectFailArgs : EventArgs, INetStructuredReason
    {
        public NetConnectFailArgs(string reason) : this(NetDisconnectMessage.Decode(reason))
        {
        }

        internal NetConnectFailArgs(NetDisconnectMessage reason)
        {
            Message = reason;
        }

        public NetDisconnectMessage Message { get; }
        public string Reason => Message.Reason;
        public bool RedialFlag => Message.RedialFlag;
    }

    public sealed class NetDisconnectedArgs : NetChannelArgs, INetStructuredReason
    {
        public NetDisconnectedArgs(INetChannel channel, string reason) : this(channel, NetDisconnectMessage.Decode(reason))
        {
        }

        internal NetDisconnectedArgs(INetChannel channel, NetDisconnectMessage reason) : base(channel)
        {
            Message = reason;
        }

        public NetDisconnectMessage Message { get; }
        public string Reason => Message.Reason;
        public bool RedialFlag => Message.RedialFlag;
    }
}
