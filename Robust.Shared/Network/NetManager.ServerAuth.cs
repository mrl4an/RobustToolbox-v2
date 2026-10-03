using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Lidgren.Network;
using Robust.Shared.AuthLib;
using Robust.Shared.Log;
using Robust.Shared.Network.Messages.Handshake;
using Robust.Shared.Utility;
using SpaceWizards.Sodium;

namespace Robust.Shared.Network
{
    partial class NetManager
    {
        private static readonly string DisconnectReasonWrongKey = new NetDisconnectMessage("Token decryption failed.\nPlease reconnect to this server from the launcher.", true).Encode();

        private readonly byte[] _cryptoPrivateKey = new byte[CryptoBox.SecretKeyBytes];

        public byte[] CryptoPublicKey { get; } = new byte[CryptoBox.PublicKeyBytes];
        public AuthMode Auth { get; private set; }

        public Func<string, Task<NetUserId?>>? AssignUserIdCallback { get; set; }
        public IServerNetManager.NetApprovalDelegate? HandleApprovalCallback { get; set; }

        private void SAGenerateKeys()
        {
            CryptoBox.KeyPair(CryptoPublicKey, _cryptoPrivateKey);

            _authLogger.Debug("Public key is {0}", Convert.ToBase64String(CryptoPublicKey));
        }

        /// <summary>
        ///     Runs the server side of the login handshake for a connection that just reached the connected state.
        ///     This is a fire-and-forget entry point: it never lets an exception escape.
        /// </summary>
        /// <param name="peer">The network peer that accepted the connection.</param>
        /// <param name="connection">The freshly connected client.</param>
        /// <remarks>
        ///     <para>
        ///         The flow has four phases: (1) read the client's login request, (2) establish an identity, either
        ///         through the encrypted/authenticated exchange with the session server or as a guest, (3) ask
        ///         content whether the connection is admitted and resolve duplicate logins, (4) send the login
        ///         success message and hand the connection over to the rest of the handshake.
        ///     </para>
        ///     <para>
        ///         Every refusal ends with <c>connection.Disconnect(reason)</c> followed by a return. Packets are read
        ///         with <see cref="AwaitData"/>, which throws <see cref="ClientDisconnectedException"/> if the client
        ///         goes away while we wait.
        ///     </para>
        /// </remarks>
        private async void HandleHandshake(NetPeerData peer, NetConnection connection)
        {
            try
            {
                _logger.Verbose($"{connection.RemoteEndPoint}: Starting handshake with peer");

                // ---- Phase 1: the client's login request is the first raw packet. ----
                var firstPacket = await AwaitData(connection);

                var msgLogin = new MsgLoginStart();
                msgLogin.ReadFromBuffer(firstPacket, _serializer);

                var remoteIp = connection.RemoteEndPoint.Address;
                var isLocal = IPAddress.IsLoopback(remoteIp) && _config.GetCVar(CVars.AuthAllowLocal);
                var canAuth = msgLogin.CanAuth;
                var needPk = msgLogin.NeedPubKey;
                // Must end with a slash: it is concatenated directly with "api/...".
                var authServer = _config.GetCVar(CVars.AuthServer);

                _logger.Verbose(
                    $"{connection.RemoteEndPoint}: Received login: canAuth={canAuth}, needPk={needPk}, " +
                    $"username={msgLogin.UserName}, encrypt={msgLogin.Encrypt}");
                _logger.Verbose($"{connection.RemoteEndPoint}: isLocal={isLocal}");

                // Servers that demand authentication only let local clients skip it.
                if (Auth == AuthMode.Required && !isLocal && !canAuth)
                {
                    connection.Disconnect("Connecting to this server requires authentication");
                    return;
                }

                NetEncryption? encryption = null;
                NetUserData userData;
                LoginType type;
                // Guest logins use an unframed success message that needs a leading marker bit; authenticated ones
                // do not, because the client already knows from the encryption request that auth is in progress.
                var padSuccessMessage = true;

                if (canAuth && Auth != AuthMode.Disabled)
                {
                    // ---- Phase 2a: authenticated login. ----

                    // Random token the client must echo back, proving it holds our public key.
                    var verifyToken = new byte[4];
                    RandomNumberGenerator.Fill(verifyToken);
                    var wantHwid = _config.GetCVar(CVars.NetHWId);

                    var msgEncReq = new MsgEncryptionRequest
                    {
                        PublicKey = needPk ? CryptoPublicKey : Array.Empty<byte>(),
                        VerifyToken = verifyToken,
                        WantHwid = wantHwid
                    };

                    var outRequest = peer.Peer.CreateMessage();
                    // The leading "false" tells the client this is not a login success message.
                    outRequest.Write(false);
                    outRequest.WritePadBits();
                    msgEncReq.WriteToBuffer(outRequest, _serializer);
                    peer.Peer.SendMessage(outRequest, connection, NetDeliveryMethod.ReliableOrdered);

                    var responsePacket = await AwaitData(connection);
                    var msgEncResponse = new MsgEncryptionResponse();
                    msgEncResponse.ReadFromBuffer(responsePacket, _serializer);

                    // Opened payload layout: [shared secret][verify token].
                    var opened = new byte[verifyToken.Length + SharedKeyLength];
                    var openedOk = CryptoBox.SealOpen(
                        opened,
                        msgEncResponse.SealedData,
                        CryptoPublicKey,
                        _cryptoPrivateKey);

                    if (!openedOk)
                    {
                        // Most likely the server restarted and generated a new key pair.
                        connection.Disconnect(DisconnectReasonWrongKey);
                        return;
                    }

                    var sharedSecret = opened.AsSpan(0, SharedKeyLength).ToArray();
                    var echoedToken = opened.AsSpan(SharedKeyLength);

                    if (!echoedToken.SequenceEqual(verifyToken))
                    {
                        connection.Disconnect("Verify token is invalid");
                        return;
                    }

                    if (msgLogin.Encrypt)
                    {
                        encryption = new NetEncryption(sharedSecret, isServer: true);
                    }

                    // Ask the session server whether this client really joined with this secret.
                    var authHash = Base64Helpers.ConvertToBase64Url(MakeAuthHash(sharedSecret, CryptoPublicKey));
                    var url = $"{authServer}api/session/hasJoined?hash={authHash}&userId={msgEncResponse.UserId}";
                    var joinedResponse = await _http.Client.GetFromJsonAsync<HasJoinedResponse>(url);

                    if (joinedResponse == null || !joinedResponse.IsValid)
                    {
                        connection.Disconnect("Failed to validate login");
                        return;
                    }

                    var joinedUser = joinedResponse.UserData!;
                    var joinedConnection = joinedResponse.ConnectionData!;

                    _logger.Verbose(
                        $"{connection.RemoteEndPoint}: Auth success, userId={joinedUser.UserId}, " +
                        $"name={joinedUser.UserName}, patronTier={joinedUser.PatronTier}");

                    var authedUserId = new NetUserId(joinedUser.UserId);

                    ImmutableArray<ImmutableArray<byte>> modernHwids =
                        [.. joinedConnection.Hwids.Select(h => ImmutableArray.Create<byte>(Convert.FromBase64String(h)))];
                    ImmutableArray<byte> legacyHwid = [.. msgEncResponse.LegacyHwid];

                    if (!wantHwid)
                    {
                        // Hardware ids were not requested, so ignore anything the client volunteered.
                        modernHwids = ImmutableArray<ImmutableArray<byte>>.Empty;
                        legacyHwid = ImmutableArray<byte>.Empty;
                    }

                    userData = new NetUserData(authedUserId, joinedUser.UserName)
                    {
                        PatronTier = joinedUser.PatronTier,
                        HWId = legacyHwid,
                        ModernHWIds = modernHwids,
                        Trust = joinedConnection.Trust,
                        CreatedTime = joinedUser.CreatedTime
                    };

                    padSuccessMessage = false;
                    type = LoginType.LoggedIn;
                }
                else
                {
                    // ---- Phase 2b: guest login (client cannot authenticate, or auth is disabled). ----
                    var requestedName = msgLogin.UserName;

                    if (!UsernameHelpers.IsNameValid(requestedName, out var invalidReason))
                    {
                        connection.Disconnect($"Username is invalid ({invalidReason.ToText()}).");
                        return;
                    }

                    // The prefix keeps guest names from colliding with names of real accounts.
                    string baseName;
                    if (Auth == AuthMode.Disabled)
                    {
                        baseName = requestedName;
                    }
                    else if (isLocal)
                    {
                        baseName = $"localhost@{requestedName}";
                    }
                    else
                    {
                        baseName = $"guest@{requestedName}";
                    }

                    // Make the name unique: base, base_2, base_3, ...
                    var finalName = baseName;
                    var counter = 1;
                    while (_assignedUsernames.ContainsKey(finalName))
                    {
                        counter += 1;
                        finalName = $"{baseName}_{counter}";
                    }

                    var (guestUserId, guestType) = await AssignUserIdAsync(finalName);

                    userData = new NetUserData(guestUserId, finalName)
                    {
                        HWId = ImmutableArray<byte>.Empty,
                        ModernHWIds = ImmutableArray<ImmutableArray<byte>>.Empty
                    };
                    type = guestType;
                }

                // ---- Phase 3: admission. ----
                var connectArgs = await OnConnecting(connection.RemoteEndPoint, userData, type);
                var deny = connectArgs.DenyReasonData;
                if (deny != null)
                {
                    var structured = new NetDisconnectMessage($"Connect denied: {deny.Text}");
                    foreach (var (key, value) in deny.AdditionalProperties)
                    {
                        structured.Values[key] = value;
                    }

                    connection.Disconnect(structured.Encode());
                    return;
                }

                // Only one connection per account: kick the older one, unless a kick is already in progress.
                if (_assignedUserIds.TryGetValue(userData.UserId, out var existing))
                {
                    if (_awaitingDisconnectToConnect.Contains(userData.UserId))
                    {
                        connection.Disconnect("Stop trying to connect multiple times at once.");
                        return;
                    }

                    _awaitingDisconnectToConnect.Add(userData.UserId);
                    try
                    {
                        existing.Disconnect("Another connection has been made with your account.");
                        await AwaitDisconnectAsync(existing);
                    }
                    finally
                    {
                        _awaitingDisconnectToConnect.Remove(userData.UserId);
                    }
                }

                // The client may have left while we were awaiting the steps above.
                if (connection.Status == NetConnectionStatus.Disconnecting ||
                    connection.Status == NetConnectionStatus.Disconnected)
                {
                    _logger.Info(
                        "{ConnectionEndpoint} ({UserId}/{UserName}) disconnected during handshake",
                        connection.RemoteEndPoint,
                        userData.UserId,
                        userData.UserName);
                    return;
                }

                // ---- Phase 4: success. ----
                var outSuccess = peer.Peer.CreateMessage();
                var msgSuccess = new MsgLoginSuccess
                {
                    UserData = userData,
                    Type = type
                };

                if (padSuccessMessage)
                {
                    // Mirror of the "false" marker of the encryption request: "true" means login success.
                    outSuccess.Write(true);
                    outSuccess.WritePadBits();
                }

                msgSuccess.WriteToBuffer(outSuccess, _serializer);
                encryption?.Encrypt(outSuccess);
                peer.Peer.SendMessage(outSuccess, connection, NetDeliveryMethod.ReliableOrdered);

                _logger.Info(
                    "Approved {ConnectionEndpoint} with username {Username} user ID {userId} into the server",
                    connection.RemoteEndPoint,
                    userData.UserName,
                    userData.UserId);

                HandleInitialHandshakeComplete(peer, connection, userData, encryption, type);
            }
            catch (ClientDisconnectedException)
            {
                _logger.Info(
                    $"Peer {NetUtility.ToHexString(connection.RemoteUniqueIdentifier)} disconnected while handshake was in-progress.");
            }
            catch (Exception e)
            {
                connection.Disconnect("Unknown server error occured during handshake.");
                _logger.Error(
                    "Exception during handshake with peer {0}:\n{1}",
                    NetUtility.ToHexString(connection.RemoteUniqueIdentifier),
                    e);
            }
        }

        private async Task<(NetUserId, LoginType)> AssignUserIdAsync(string username)
        {
            if (AssignUserIdCallback == null)
            {
                goto unassigned;
            }

            var assigned = await AssignUserIdCallback(username);
            if (assigned != null)
            {
                return (assigned.Value, LoginType.GuestAssigned);
            }

            unassigned:
            // Just generate a random new GUID.
            var uid = new NetUserId(Guid.NewGuid());
            return (uid, LoginType.Guest);
        }

        private Task AwaitDisconnectAsync(NetConnection connection)
        {
            if (!_awaitingDisconnect.TryGetValue(connection, out var tcs))
            {
                tcs = new TaskCompletionSource<object?>();
                _awaitingDisconnect.Add(connection, tcs);
            }

            return tcs.Task;
        }

        private async void HandleApproval(NetIncomingMessage message)
        {
            DebugTools.Assert(message.SenderConnection != null);
            // TODO: Maybe preemptively refuse connections here in some cases?
            if (message.SenderConnection.Status != NetConnectionStatus.RespondedAwaitingApproval)
            {
                // This can happen if the approval message comes in after the state changes to disconnected.
                // In that case just ignore it.
                return;
            }

            if (HandleApprovalCallback != null)
            {
                var approval = await HandleApprovalCallback(new NetApprovalEventArgs(message.SenderConnection));

                if (!approval.IsApproved)
                {
                    message.SenderConnection.Deny(approval.DenyReason);
                    return;
                }
            }

            message.SenderConnection.Approve();
        }

        // ReSharper disable ClassNeverInstantiated.Local
        private sealed record HasJoinedResponse(bool IsValid, HasJoinedUserData? UserData, HasJoinedConnectionData? ConnectionData);
        private sealed record HasJoinedUserData(string UserName, Guid UserId, string? PatronTier, DateTime CreatedTime);
        private sealed record HasJoinedConnectionData(string[] Hwids, float Trust);
        // ReSharper restore ClassNeverInstantiated.Local
    }
}
