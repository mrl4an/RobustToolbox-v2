using System;
using System.Collections.Generic;
using System.IO;
using Robust.Shared.Timing;

namespace Robust.Shared.Configuration
{
    /// <summary>
    /// Additional information about a CVar change.
    /// </summary>
    public readonly struct CVarChangeInfo
    {
        /// <summary>
        /// The name of the cvar that was changed.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The tick this CVar changed at.
        /// </summary>
        /// <remarks>
        /// Nominally this should always be the current tick,
        /// however due to poor network conditions it is possible for CVars to get applied late.
        /// In this case, this is effectively "this is the tick it was SUPPOSED to have been applied at".
        /// </remarks>
        public readonly GameTick TickChanged;

        /// <summary>
        /// The new value.
        /// </summary>
        public readonly object NewValue;

        /// <summary>
        /// The previous value.
        /// </summary>
        public readonly object OldValue;

        internal CVarChangeInfo(string name, GameTick tickChanged, object newValue, object oldValue)
        {
            Name = name;
            TickChanged = tickChanged;
            NewValue = newValue;
            OldValue = oldValue;
        }
    }

    public delegate void CVarChanged<in T>(T newValue, in CVarChangeInfo info);

    /// <summary>
    /// Central access point for CVars (configuration variables): registering them, reading and changing their values, and persisting them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accessing (getting/setting) main CVars is thread safe.
    /// Note that value-changed callbacks are ran synchronously from the thread using <see cref="SetCVar"/>,
    /// so it is not recommended to modify CVars from other threads.
    /// </para>
    /// </remarks>
    [NotContentImplementable]
    public interface IConfigurationManager
    {
        /// <summary>
        ///     Writes the current configuration to the config file on disk so that it survives a restart.
        /// </summary>
        /// <remarks>
        ///     Only CVars flagged as archivable (<see cref="CVar.ARCHIVE"/>) and not simply sitting at an
        ///     unmodified default are expected to be persisted. Pending rollback data
        ///     (see <see cref="MarkForRollback(string[])"/>) is stored in the file as well.
        /// </remarks>
        void SaveToFile();

        /// <summary>
        /// Serializes a list of cvars to a toml.
        /// </summary>
        void SaveToTomlStream(Stream stream, IEnumerable<string> cvars);

        HashSet<string> LoadFromTomlStream(Stream stream);

        /// <summary>
        /// Load a TOML config file and use the CVar values specified as an <see cref="OverrideDefault"/>.
        /// </summary>
        /// <remarks>
        /// All CVars in the TOML file must be registered when this function is called.
        /// </remarks>
        /// <returns>A set of all CVars touched.</returns>
        HashSet<string> LoadDefaultsFromTomlStream(Stream stream);

        /// <summary>
        ///     Declares a CVar so that it can be read and written. A CVar must be registered before it is
        ///     read or modified.
        /// </summary>
        /// <remarks>
        ///     If a value for this name was already supplied before registration (for example from a config
        ///     file or a command-line override), the CVar starts with that value instead of
        ///     <paramref name="defaultValue"/>, and the default is kept as the fallback.
        /// </remarks>
        /// <param name="name">The full dotted name of the CVar. Dots are reserved for nesting in the config file.</param>
        /// <param name="defaultValue">
        ///     The value used when nothing else has supplied one. Its type <typeparamref name="T"/> also
        ///     fixes the type of the CVar.
        /// </param>
        /// <param name="flags">Flags describing persistence and replication behavior. Defaults to none.</param>
        /// <param name="onValueChanged">
        ///     Optional callback invoked with the new value whenever the CVar changes. The subscription lasts
        ///     for the lifetime of the manager. Pass null to not subscribe.
        /// </param>
        /// <typeparam name="T">The type of value stored in the CVar.</typeparam>
        void RegisterCVar<T>(string name, T defaultValue, CVar flags = CVar.NONE, Action<T>? onValueChanged = null)
            where T : notnull;

        /// <summary>
        ///     Checks whether a CVar with exactly this name has been registered.
        /// </summary>
        /// <remarks>
        ///     Name matching is exact and case-sensitive. This never throws for unknown names. A value that
        ///     was only loaded from a config file but not yet registered does not count as registered.
        /// </remarks>
        /// <param name="name">The CVar name to look up.</param>
        /// <returns>True if the CVar has been registered, false otherwise.</returns>
        bool IsCVarRegistered(string name);

        /// <summary>
        /// Get the CVar flags of a registered cvar.
        /// </summary>
        CVar GetCVarFlags(string name);

        /// <summary>
        /// Gets a list of all registered cvars
        /// </summary>
        /// <returns></returns>
        IEnumerable<string> GetRegisteredCVars();

        /// <summary>
        ///     Assigns a new value to a CVar by name.
        /// </summary>
        /// <remarks>
        ///     If the stored value actually changes, change callbacks and <see cref="OnCVarValueChanged"/>
        ///     run synchronously on the calling thread. Assigning an equal value need not notify anyone.
        ///     If the name is not registered yet, the value is remembered and applied once the CVar is
        ///     registered; this is how config files and command-line overrides are loaded.
        /// </remarks>
        /// <param name="name">Name of the CVar to change.</param>
        /// <param name="value">
        ///     The new value, boxed. It should be of the CVar's registered type; compatible numeric or enum
        ///     representations may be converted by the implementation.
        /// </param>
        /// <param name="force">
        ///     When false, writes that the CVar's flags and the current network role do not allow (for
        ///     example a client changing a server-authoritative replicated CVar) are rejected. When true,
        ///     the write is applied regardless of such restrictions.
        /// </param>
        void SetCVar(string name, object value, bool force = false);

        void SetCVar<T>(CVarDef<T> def, T value, bool force = false) where T : notnull;

        /// <summary>
        /// Change the default value for a CVar.
        /// This means the CVar value is changed *only* if it has not been changed by config file or <c>OverrideConVars</c>.
        /// </summary>
        /// <param name="name">The name of the CVar to change the default for.</param>
        /// <param name="value">The new default value of the CVar.</param>
        void OverrideDefault(string name, object value);

        /// <summary>
        /// Change the default value for a CVar.
        /// This means the CVar value is changed *only* if it has not been changed by config file or <c>OverrideConVars</c>.
        /// </summary>
        /// <param name="def">The definition of the CVar to change the default for.</param>
        /// <param name="value">The new default value of the CVar.</param>
        void OverrideDefault<T>(CVarDef<T> def, T value) where T : notnull;

        /// <summary>
        ///     Gets the current value of a CVar as a boxed object of its registered type.
        /// </summary>
        /// <remarks>
        ///     Returns the value set through a config file, override or <see cref="SetCVar(string,object,bool)"/>
        ///     if there is one; otherwise the registered default, including any default changed with
        ///     <see cref="OverrideDefault(string,object)"/>. For an unregistered name an exception is thrown
        ///     instead of returning null.
        /// </remarks>
        /// <param name="name">Name of the CVar to read.</param>
        /// <returns>The current value of the CVar.</returns>
        object GetCVar(string name);

        /// <summary>
        ///     Gets the current value of a CVar, typed as <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        ///     Resolves the value the same way as <see cref="GetCVar(string)"/>: the current value if one
        ///     was set, otherwise the registered default. For an unregistered name an exception is thrown.
        /// </remarks>
        /// <param name="name">Name of the CVar to read.</param>
        /// <typeparam name="T">The type to return the value as; it should match the CVar's registered type.</typeparam>
        /// <returns>The current value of the CVar.</returns>
        T GetCVar<T>(string name);

        T GetCVar<T>(CVarDef<T> def) where T : notnull;

        /// <summary>
        ///     Gets the type of a value stored in a CVar.
        /// </summary>
        /// <param name="name">The name of the CVar</param>
        Type GetCVarType(string name);

        /// <summary>
        /// Listen for an event for if the config value changes.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="cVar">The CVar to listen for.</param>
        /// <param name="onValueChanged">The delegate to run when the value was changed.</param>
        /// <param name="invokeImmediately">
        /// Whether to run the callback immediately in this method. Can help reduce boilerplate
        /// </param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        /// <seealso cref="UnsubValueChanged{T}(Robust.Shared.Configuration.CVarDef{T},System.Action{T})"/>
        void OnValueChanged<T>(CVarDef<T> cVar, Action<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull;

        /// <summary>
        /// Listen for an event for if the config value changes.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="name">The name of the CVar to listen for.</param>
        /// <param name="onValueChanged">The delegate to run when the value was changed.</param>
        /// <param name="invokeImmediately">
        /// Whether to run the callback immediately in this method. Can help reduce boilerplate
        /// </param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        /// <seealso cref="UnsubValueChanged{T}(string,System.Action{T})"/>
        void OnValueChanged<T>(string name, Action<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull;

        /// <summary>
        /// Unsubscribe an event previously registered with <see cref="OnValueChanged{T}(Robust.Shared.Configuration.CVarDef{T},System.Action{T},bool)"/>.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="cVar">The CVar to unsubscribe from.</param>
        /// <param name="onValueChanged">The delegate to unsubscribe.</param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        void UnsubValueChanged<T>(CVarDef<T> cVar, Action<T> onValueChanged)
            where T : notnull;

        /// <summary>
        /// Unsubscribe an event previously registered with <see cref="OnValueChanged{T}(string,System.Action{T},bool)"/>.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="name">The name of the CVar to unsubscribe from.</param>
        /// <param name="onValueChanged">The delegate to unsubscribe.</param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        void UnsubValueChanged<T>(string name, Action<T> onValueChanged)
            where T : notnull;

        /// <summary>
        /// Listen for an event for if the config value changes.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="cVar">The CVar to listen for.</param>
        /// <param name="onValueChanged">The delegate to run when the value was changed.</param>
        /// <param name="invokeImmediately">
        /// Whether to run the callback immediately in this method. Can help reduce boilerplate
        /// </param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        /// <seealso cref="UnsubValueChanged{T}(Robust.Shared.Configuration.CVarDef{T},System.Action{T})"/>
        void OnValueChanged<T>(CVarDef<T> cVar, CVarChanged<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull;

        /// <summary>
        /// Listen for an event for if the config value changes.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="name">The name of the CVar to listen for.</param>
        /// <param name="onValueChanged">The delegate to run when the value was changed.</param>
        /// <param name="invokeImmediately">
        /// Whether to run the callback immediately in this method. Can help reduce boilerplate
        /// </param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        /// <seealso cref="UnsubValueChanged{T}(string,System.Action{T})"/>
        void OnValueChanged<T>(string name, CVarChanged<T> onValueChanged, bool invokeImmediately = false)
            where T : notnull;

        /// <summary>
        /// Unsubscribe an event previously registered with <see cref="OnValueChanged{T}(Robust.Shared.Configuration.CVarDef{T},System.Action{T},bool)"/>.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="cVar">The CVar to unsubscribe from.</param>
        /// <param name="onValueChanged">The delegate to unsubscribe.</param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        void UnsubValueChanged<T>(CVarDef<T> cVar, CVarChanged<T> onValueChanged)
            where T : notnull;

        /// <summary>
        /// Unsubscribe an event previously registered with <see cref="OnValueChanged{T}(string,System.Action{T},bool)"/>.
        /// </summary>
        /// <remarks>
        /// This is an O(n) operation.
        /// </remarks>
        /// <param name="name">The name of the CVar to unsubscribe from.</param>
        /// <param name="onValueChanged">The delegate to unsubscribe.</param>
        /// <typeparam name="T">The type of value contained in this CVar.</typeparam>
        void UnsubValueChanged<T>(string name, CVarChanged<T> onValueChanged)
            where T : notnull;

        public event Action<CVarChangeInfo>? OnCVarValueChanged;

        //
        // Rollback
        //

        /// <summary>
        /// Snapshot a CVar to be rolled back later, even in the event of a client crash.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This set of APIs is intended for settings menus that want to show the user a
        /// "Do these settings look correct?" prompt with timeout,
        /// so that settings can be rolled back even in the event of alt+F4 or client crash.
        /// </para>
        /// <para>
        /// Rollback is applied on <see cref="ApplyRollback"/> call or client restart,
        /// unless CVars are unmarked again via <see cref="UnmarkForRollback(Robust.Shared.Configuration.CVarDef[])"/>
        /// </para>
        /// <para>
        /// Rollback is tracked in the config file too, and this command does not save it automatically. Of course,
        /// not saving the config file before client exit also effectively rolls back CVars.
        /// </para>
        /// <para>
        /// Calling this method if a CVar is already marked for rollback will simply update the snapshot value.
        /// </para>
        /// </remarks>
        /// <param name="cVars">The CVars to roll back.</param>
        /// <seealso cref="UnmarkForRollback(Robust.Shared.Configuration.CVarDef[])"/>
        void MarkForRollback(params CVarDef[] cVars);

        /// <summary>
        /// Snapshot a CVar to be rolled back later, even in the event of a client crash.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This set of APIs is intended for settings menus that want to show the user a
        /// "Do these settings look correct?" prompt with timeout,
        /// so that settings can be rolled back even in the event of alt+F4 or client crash.
        /// </para>
        /// <para>
        /// Rollback is applied on <see cref="ApplyRollback"/> call or client restart,
        /// unless CVars are unmarked again via <see cref="UnmarkForRollback(string[])"/>
        /// </para>
        /// <para>
        /// Rollback is tracked in the config file too, and this command does not save it automatically. Of course,
        /// not saving the config file before client exit also effectively rolls back CVars.
        /// </para>
        /// <para>
        /// Calling this method if a CVar is already marked for rollback will simply update the snapshot value.
        /// </para>
        /// </remarks>
        /// <param name="cVars">The CVar names to snapshot and (possibly) roll back later.</param>
        /// <seealso cref="UnmarkForRollback(string[])"/>
        void MarkForRollback(params string[] cVars);

        /// <summary>
        /// Unmark a CVar for rollback.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This set of APIs is intended for settings menus that want to show the user a
        /// "Do these settings look correct?" prompt with timeout,
        /// so that settings can be rolled back even in the event of alt+F4 or client crash.
        /// </para>
        /// <para>
        /// Rollback is tracked in the config file too, and this command does not save it automatically.
        /// Users must still call <see cref="SaveToFile"/> manually to avoid rollback happening.
        /// </para>
        /// </remarks>
        /// <param name="cVars">The CVars to unmark for rollback.</param>
        /// <seealso cref="MarkForRollback(Robust.Shared.Configuration.CVarDef[])"/>
        /// <seealso cref="ApplyRollback"/>
        void UnmarkForRollback(params CVarDef[] cVars);

        /// <summary>
        /// Unmark a CVar for rollback.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This set of APIs is intended for settings menus that want to show the user a
        /// "Do these settings look correct?" prompt with timeout,
        /// so that settings can be rolled back even in the event of alt+F4 or client crash.
        /// </para>
        /// <para>
        /// Rollback is tracked in the config file too, and this command does not save it automatically.
        /// Users must still call <see cref="SaveToFile"/> manually to avoid rollback happening.
        /// </para>
        /// </remarks>
        /// <param name="cVars">The CVars to unmark for rollback.</param>
        /// <seealso cref="MarkForRollback(string[])"/>
        /// <seealso cref="ApplyRollback"/>
        void UnmarkForRollback(params string[] cVars);

        /// <summary>
        /// Apply all pending CVar rollbacks.
        /// </summary>
        /// <para>
        /// This set of APIs is intended for settings menus that want to show the user a
        /// "Do these settings look correct?" prompt with timeout,
        /// so that settings can be rolled back even in the event of alt+F4 or client crash.
        /// </para>
        /// <remarks>
        /// This implicitly saves the config file to ensure the config file does not contain
        /// rollback data for longer than necessary.
        /// </remarks>
        /// <seealso cref="MarkForRollback(Robust.Shared.Configuration.CVarDef[])"/>
        /// <seealso cref="UnmarkForRollback(Robust.Shared.Configuration.CVarDef[])"/>
        void ApplyRollback();
    }
}
