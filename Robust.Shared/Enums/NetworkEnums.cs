namespace Robust.Shared.Enums
{
    /// <summary>
    ///     Identifies the kind of a placement network message. The placement system lets a client
    ///     ask the server to put an entity or tile into the world (and to remove them again), and lets
    ///     the server drive the client's placement mode.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The value is sent as the first byte of the placement message; the rest of the payload
    ///         depends on which member it is. It is written as a single raw byte and read back by casting
    ///         the byte to this type, with no range check. A byte that matches no member is simply
    ///         ignored by the code switching over it.
    ///     </para>
    ///     <para>
    ///         Because the numeric values travel over the network, the declaration order and the
    ///         <see cref="byte"/> underlying type must not change. New members may only be appended at the end.
    ///     </para>
    /// </remarks>
    public enum PlacementManagerMessage : byte
    {
        /// <summary>
        ///     Sent by the server to tell a client to enter placement mode. The payload carries the
        ///     range (32-bit integer), a flag saying whether a tile is being placed, the object type
        ///     (string) and the alignment option (string).
        /// </summary>
        StartPlacement,

        /// <summary>
        ///     Instructs a client to leave placement mode. This is handled inside the placement managers
        ///     themselves; the message serializer does not implement reading or writing this value, so it
        ///     is not expected to go over the wire.
        /// </summary>
        CancelPlacement,

        /// <summary>
        ///     Reserved, currently unused. The message serializer does not implement it and the placement
        ///     managers do not otherwise handle it.
        /// </summary>
        PlacementFailed,

        /// <summary>
        ///     Sent by a client to ask the server to place an entity or tile. The payload carries the
        ///     alignment (string), a tile flag, a replacement flag, then either the tile id (integer) or
        ///     the entity prototype name (string), followed by the network coordinates, the direction
        ///     (one byte) and a mirrored flag.
        /// </summary>
        RequestPlacement,

        /// <summary>
        ///     Sent by a client to ask the server to delete a single entity. The payload carries the
        ///     network entity id as a 32-bit integer.
        /// </summary>
        RequestEntRemove,

        /// <summary>
        ///     Sent by a client to ask the server to delete everything inside a rectangle. The payload
        ///     carries the network coordinates of the rectangle and a two-dimensional size vector.
        /// </summary>
        RequestRectRemove,
    }

    /// <summary>
    ///     The connection lifecycle state of a player session.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The numeric order is part of the contract: code compares statuses relationally, for
    ///         example "at least <see cref="Disconnected"/>" to decide a session is unusable, or
    ///         "later than <see cref="Connecting"/>" to decide the client console is available.
    ///         The order <see cref="Zombie"/> &lt; <see cref="Connecting"/> &lt; <see cref="Connected"/>
    ///         &lt; <see cref="InGame"/> &lt; <see cref="Disconnected"/> must therefore be kept.
    ///     </para>
    ///     <para>
    ///         The value is written as a single raw byte in the player list network message and is
    ///         also part of the session state replicated to clients, where enums are encoded by their
    ///         numeric value. The <see cref="byte"/> underlying type and the member values must not change.
    ///         New members may only be appended at the end.
    ///     </para>
    /// </remarks>
    public enum SessionStatus : byte
    {
        /// <summary>
        ///     Placeholder state meaning the session does not represent a live connection. It is also
        ///     the default value of the enum.
        /// </summary>
        Zombie = 0,

        /// <summary>
        ///     The connection handshake is in progress. This is the initial status of newly created
        ///     sessions.
        /// </summary>
        Connecting = 1,

        /// <summary>
        ///     The player is connected and authenticated but has not entered the game proper, for
        ///     example while sitting in a lobby.
        /// </summary>
        Connected = 2,

        /// <summary>
        ///     The player is fully in the game. The server only sends world state to sessions in this
        ///     status.
        /// </summary>
        InGame = 3,

        /// <summary>
        ///     The connection is gone. Many systems clean up after a session when it changes to this
        ///     status.
        /// </summary>
        Disconnected = 4,
    }
}
