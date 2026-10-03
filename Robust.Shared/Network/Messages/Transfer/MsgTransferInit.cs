using Lidgren.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Serialization;

namespace Robust.Shared.Network.Messages.Transfer;

internal sealed class MsgTransferInit : NetMessage
{
    public (string EndpointUrl, byte[] Key)? HttpInfo;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        // Wire layout: a flag bit; when set, byte-align, then the endpoint URL string
        // followed by a fixed-size key with no length prefix.
        var hasHttp = buffer.ReadBoolean();
        if (!hasHttp)
        {
            HttpInfo = null;
            return;
        }

        buffer.SkipPadBits();
        var endpointUrl = buffer.ReadString();
        var keyBytes = buffer.ReadBytes(TransferImplWebSocket.RandomKeyBytes);
        HttpInfo = (endpointUrl, keyBytes);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        if (HttpInfo is null)
        {
            buffer.Write(false);
            return;
        }

        buffer.Write(true);
        buffer.WritePadBits();

        var (ep, key) = HttpInfo.Value;
        buffer.Write(ep);
        buffer.Write(key);
    }
}
