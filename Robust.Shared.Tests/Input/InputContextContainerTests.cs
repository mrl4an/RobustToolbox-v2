using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Robust.Shared.Input;

namespace Robust.Shared.Tests.Input;

/// <summary>
///     Tests for <see cref="InputContextContainer"/> and <see cref="ContextChangedEventArgs"/>.
/// </summary>
/// <remarks>
///     The container fires <see cref="IInputContextContainer.ContextChanged"/> once from its own constructor, but
///     nobody can be subscribed at that point, so that event is not observable and is not tested here.
///
///     Several tests deliberately make two validation conditions true at the same time (for example an empty
///     name and a missing parent) to pin down which check is reported first. The two <c>New</c> overloads
///     validate in different orders, so each overload has its own set of such tests.
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(InputContextContainer))]
internal sealed class InputContextContainerTests
{
    /// <summary>
    ///     One recorded <see cref="IInputContextContainer.ContextChanged"/> invocation.
    /// </summary>
    private readonly record struct RaisedEvent(object? Sender, IInputCmdContext? OldContext, IInputCmdContext? NewContext);

    /// <summary>
    ///     Subscribes to <see cref="IInputContextContainer.ContextChanged"/> and returns the list that receives
    ///     every invocation in the order it happened.
    /// </summary>
    private static List<RaisedEvent> Record(InputContextContainer container)
    {
        var events = new List<RaisedEvent>();
        container.ContextChanged += (sender, args) => events.Add(new RaisedEvent(sender, args.OldContext, args.NewContext));
        return events;
    }

    #region Construction

    [Test]
    public void NewContainer_HasCommonContextActive()
    {
        var container = new InputContextContainer();

        Assert.That(container.ActiveContext.Name, Is.EqualTo("common"));
    }

    [Test]
    public void NewContainer_CommonContextIsRegisteredAndIsTheActiveInstance()
    {
        var container = new InputContextContainer();

        Assert.Multiple(() =>
        {
            Assert.That(container.Exists("common"), Is.True);
            Assert.That(container.GetContext("common"), Is.SameAs(container.ActiveContext));
        });
    }

    [Test]
    public void NewContainer_DeferringIsDisabled()
    {
        var container = new InputContextContainer();

        Assert.That(container.DeferringEnabled, Is.False);
    }

    [Test]
    public void NewContainer_CommonContextHasNoFunctions()
    {
        var container = new InputContextContainer();

        Assert.That(container.GetContext("common").ToArray(), Is.Empty);
    }

    #endregion

    #region New(string, string)

    [Test]
    public void New_WithParentName_ReturnsContextWithGivenName()
    {
        var container = new InputContextContainer();

        var context = container.New("child", "common");

        Assert.That(context.Name, Is.EqualTo("child"));
    }

    [Test]
    public void New_WithParentName_ReturnsSameInstanceAsGetContext()
    {
        var container = new InputContextContainer();

        var created = container.New("child", "common");

        Assert.Multiple(() =>
        {
            Assert.That(container.Exists("child"), Is.True);
            Assert.That(container.GetContext("child"), Is.SameAs(created));
        });
    }

    [Test]
    public void New_StartsWithNoFunctions()
    {
        var container = new InputContextContainer();
        container.GetContext("common").AddFunction("FuncA");

        var child = container.New("child", "common");

        // Only the parent owns FuncA; the child's own function list is empty.
        Assert.That(child.ToArray(), Is.Empty);
    }

    [Test]
    public void New_PreservesNameExactly()
    {
        // Names are not trimmed or case-folded. The name below is not whitespace-only, so it is valid, and both
        // the returned context and the lookup key must keep the surrounding spaces and the casing.
        var container = new InputContextContainer();

        var context = container.New("  Mixed Case  ", "common");

        Assert.Multiple(() =>
        {
            Assert.That(context.Name, Is.EqualTo("  Mixed Case  "));
            Assert.That(container.Exists("  Mixed Case  "), Is.True);
            Assert.That(container.Exists("Mixed Case"), Is.False);
            Assert.That(container.Exists("  mixed case  "), Is.False);
        });
    }

    [Test]
    public void New_DoesNotRaiseContextChangedOrChangeActiveContext()
    {
        var container = new InputContextContainer();
        var originalActive = container.ActiveContext;
        var events = Record(container);

        container.New("child", "common");

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Empty);
            Assert.That(container.ActiveContext, Is.SameAs(originalActive));
        });
    }

    [Test]
    public void New_WithParentName_CanUseUserCreatedContextAsParent()
    {
        var container = new InputContextContainer();
        container.New("parent", "common");

        var child = container.New("child", "parent");

        Assert.That(container.GetContext("child"), Is.SameAs(child));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    public void New_WithParentName_NullOrWhitespaceUniqueName_ThrowsArgumentException(string? uniqueName)
    {
        var container = new InputContextContainer();

        // Assert.Throws demands the exact type, so this also proves that a null name is reported as a plain
        // ArgumentException and not as an ArgumentNullException.
        var ex = Assert.Throws<ArgumentException>(() => container.New(uniqueName!, "common"));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void New_WithParentName_NullOrWhitespaceParentName_ThrowsArgumentException(string? parentName)
    {
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("child", parentName!));

        Assert.That(ex!.ParamName, Is.EqualTo("parentName"));
    }

    [Test]
    public void New_WithParentName_MissingParent_ThrowsArgumentException()
    {
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("child", "doesNotExist"));

        Assert.That(ex!.ParamName, Is.EqualTo("parentName"));
    }

    [Test]
    public void New_WithParentName_ParentLookupIsCaseSensitive()
    {
        // "Common" differs from "common" only by case and is therefore a different (non-existent) name.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("child", "Common"));

        Assert.That(ex!.ParamName, Is.EqualTo("parentName"));
    }

    [Test]
    public void New_WithParentName_DuplicateName_ThrowsArgumentException()
    {
        var container = new InputContextContainer();
        container.New("child", "common");

        var ex = Assert.Throws<ArgumentException>(() => container.New("child", "common"));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentName_CommonName_ThrowsArgumentException()
    {
        // The default context always exists, so its name can never be created again.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("common", "common"));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentName_DuplicateNameCheckIsCaseSensitive()
    {
        // "Common" is a different name from "common", so creating it succeeds.
        var container = new InputContextContainer();

        Assert.DoesNotThrow(() => container.New("Common", "common"));
    }

    [Test]
    public void New_WithParentName_UniqueNameCheckedBeforeParentName()
    {
        // Both names are invalid (uniqueName is whitespace, parentName is null). The uniqueName check comes first.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New(" ", (string) null!));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentName_InvalidUniqueNameCheckedBeforeMissingParent()
    {
        // uniqueName is empty AND the parent does not exist. The invalid name is reported.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("", "doesNotExist"));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentName_InvalidUniqueNameCheckedBeforeDuplicate()
    {
        // The empty name is invalid; it cannot be a duplicate either, so this mainly guards that the whitespace
        // check runs before any dictionary access (an empty name must not fall through to the lookup).
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("", ""));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentName_InvalidParentNameCheckedBeforeDuplicate()
    {
        // "common" is a duplicate AND parentName is null. The parentName check precedes the duplicate check.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("common", (string) null!));

        Assert.That(ex!.ParamName, Is.EqualTo("parentName"));
    }

    [Test]
    public void New_WithParentName_MissingParentCheckedBeforeDuplicate()
    {
        // "common" is a duplicate AND the parent does not exist. The missing parent is reported first.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("common", "doesNotExist"));

        Assert.That(ex!.ParamName, Is.EqualTo("parentName"));
    }

    [Test]
    public void New_WithParentName_FailedCall_DoesNotRegisterContext()
    {
        var container = new InputContextContainer();

        Assert.Throws<ArgumentException>(() => container.New("child", "doesNotExist"));

        Assert.That(container.Exists("child"), Is.False);
    }

    #endregion

    #region New(string, IInputCmdContext)

    [Test]
    public void New_WithParentObject_ReturnsRegisteredContext()
    {
        var container = new InputContextContainer();
        var parent = container.GetContext("common");

        var child = container.New("child", parent);

        Assert.Multiple(() =>
        {
            Assert.That(child.Name, Is.EqualTo("child"));
            Assert.That(container.GetContext("child"), Is.SameAs(child));
        });
    }

    [Test]
    public void New_WithParentObject_ChildSeesParentFunctionsThroughHierarchy()
    {
        var container = new InputContextContainer();
        var parent = container.GetContext("common");
        parent.AddFunction("FuncA");

        var child = container.New("child", parent);

        Assert.Multiple(() =>
        {
            Assert.That(child.FunctionExistsHierarchy("FuncA"), Is.True);
            Assert.That(child.FunctionExists("FuncA"), Is.False);
        });
    }

    [Test]
    public void New_WithParentObject_AcceptsParentThatIsNotRegisteredInThisContainer()
    {
        // The parent is only used through IInputCmdContext. It does not need to be registered in this
        // container, so a context that was created elsewhere is a valid parent.
        var container = new InputContextContainer();
        var foreignParent = new InputCmdContext("foreign");
        foreignParent.AddFunction("ForeignFunc");

        var child = container.New("child", foreignParent);

        Assert.Multiple(() =>
        {
            Assert.That(container.Exists("foreign"), Is.False);
            Assert.That(child.FunctionExistsHierarchy("ForeignFunc"), Is.True);
        });
    }

    [Test]
    public void New_WithParentObject_AcceptsParentFromAnotherContainer()
    {
        var container = new InputContextContainer();
        var otherContainer = new InputContextContainer();
        var otherParent = otherContainer.New("other", "common");
        otherParent.AddFunction("OtherFunc");

        var child = container.New("child", otherParent);

        Assert.That(child.FunctionExistsHierarchy("OtherFunc"), Is.True);
    }

    [Test]
    public void New_WithParentObject_AcceptsParentThatWasRemoved()
    {
        var container = new InputContextContainer();
        var parent = container.New("parent", "common");
        parent.AddFunction("FuncA");
        container.Remove("parent");

        var child = container.New("child", parent);

        Assert.That(child.FunctionExistsHierarchy("FuncA"), Is.True);
    }

    [Test]
    public void New_WithParentObject_DoesNotRaiseContextChanged()
    {
        var container = new InputContextContainer();
        var events = Record(container);

        container.New("child", container.GetContext("common"));

        Assert.That(events, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void New_WithParentObject_NullOrWhitespaceUniqueName_ThrowsArgumentException(string? uniqueName)
    {
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New(uniqueName!, container.GetContext("common")));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentObject_DuplicateName_ThrowsArgumentException()
    {
        var container = new InputContextContainer();
        var parent = container.GetContext("common");
        container.New("child", parent);

        var ex = Assert.Throws<ArgumentException>(() => container.New("child", parent));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentObject_NullParent_ThrowsArgumentNullException()
    {
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentNullException>(() => container.New("child", (IInputCmdContext) null!));

        Assert.That(ex!.ParamName, Is.EqualTo("parent"));
    }

    [Test]
    public void New_WithParentObject_FailedNullParentCall_DoesNotRegisterContext()
    {
        var container = new InputContextContainer();

        Assert.Throws<ArgumentNullException>(() => container.New("child", (IInputCmdContext) null!));

        Assert.That(container.Exists("child"), Is.False);
    }

    [Test]
    public void New_WithParentObject_InvalidNameCheckedBeforeNullParent()
    {
        // The name is whitespace AND the parent is null. The name is reported, as an ArgumentException
        // (Assert.Throws requires the exact type, so an ArgumentNullException would fail this test).
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New(" ", (IInputCmdContext) null!));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    [Test]
    public void New_WithParentObject_DuplicateNameCheckedBeforeNullParent()
    {
        // This overload differs from the string overload: "common" is a duplicate AND the parent is null, and
        // the duplicate is reported first, as an ArgumentException rather than an ArgumentNullException.
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.New("common", (IInputCmdContext) null!));

        Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
    }

    #endregion

    #region Hierarchy created by New

    [Test]
    public void New_ChainOfContexts_FunctionsAreVisibleThroughAllAncestors()
    {
        // Chain: common <- middle <- leaf. FuncA lives only in "common", FuncB only in "middle".
        var container = new InputContextContainer();
        container.New("middle", "common");
        container.New("leaf", "middle");
        container.GetContext("common").AddFunction("FuncA");
        container.GetContext("middle").AddFunction("FuncB");

        var leaf = container.GetContext("leaf");
        var middle = container.GetContext("middle");
        var common = container.GetContext("common");

        Assert.Multiple(() =>
        {
            Assert.That(leaf.FunctionExistsHierarchy("FuncA"), Is.True);
            Assert.That(leaf.FunctionExistsHierarchy("FuncB"), Is.True);
            Assert.That(middle.FunctionExistsHierarchy("FuncA"), Is.True);

            // Ancestors never see functions registered on their descendants.
            Assert.That(common.FunctionExistsHierarchy("FuncB"), Is.False);
            Assert.That(leaf.FunctionExistsHierarchy("Unknown"), Is.False);
        });
    }

    [Test]
    public void New_SiblingContexts_DoNotSeeEachOthersFunctions()
    {
        var container = new InputContextContainer();
        var left = container.New("left", "common");
        var right = container.New("right", "common");
        left.AddFunction("LeftFunc");

        Assert.That(right.FunctionExistsHierarchy("LeftFunc"), Is.False);
    }

    [Test]
    public void Remove_ParentContext_ChildKeepsItsParentReference()
    {
        // The container does not track parent/child links, so removing the parent from the container must not
        // detach it from the child.
        var container = new InputContextContainer();
        var parent = container.New("parent", "common");
        var child = container.New("child", "parent");
        parent.AddFunction("FuncA");

        container.Remove("parent");

        Assert.That(child.FunctionExistsHierarchy("FuncA"), Is.True);
    }

    #endregion

    #region Exists / GetContext / TryGetContext

    [Test]
    public void Exists_UnknownName_ReturnsFalse()
    {
        var container = new InputContextContainer();

        Assert.That(container.Exists("doesNotExist"), Is.False);
    }

    [Test]
    public void Exists_IsCaseSensitive()
    {
        var container = new InputContextContainer();

        Assert.Multiple(() =>
        {
            Assert.That(container.Exists("common"), Is.True);
            Assert.That(container.Exists("Common"), Is.False);
            Assert.That(container.Exists("COMMON"), Is.False);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Exists_EmptyOrWhitespaceName_ReturnsFalse(string name)
    {
        var container = new InputContextContainer();

        Assert.That(container.Exists(name), Is.False);
    }

    [Test]
    public void Exists_NullName_ThrowsArgumentNullException()
    {
        var container = new InputContextContainer();

        Assert.Throws<ArgumentNullException>(() => container.Exists(null!));
    }

    [Test]
    public void GetContext_ExistingName_ReturnsContext()
    {
        var container = new InputContextContainer();
        var created = container.New("child", "common");

        Assert.That(container.GetContext("child"), Is.SameAs(created));
    }

    [Test]
    public void GetContext_UnknownName_ThrowsKeyNotFoundException()
    {
        var container = new InputContextContainer();

        Assert.Throws<KeyNotFoundException>(() => container.GetContext("doesNotExist"));
    }

    [Test]
    public void GetContext_NullName_ThrowsArgumentNullException()
    {
        var container = new InputContextContainer();

        Assert.Throws<ArgumentNullException>(() => container.GetContext(null!));
    }

    [Test]
    public void TryGetContext_ExistingName_ReturnsTrueAndContext()
    {
        var container = new InputContextContainer();
        var created = container.New("child", "common");

        var found = container.TryGetContext("child", out var context);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(context, Is.SameAs(created));
        });
    }

    [Test]
    public void TryGetContext_UnknownName_ReturnsFalseAndNullContext()
    {
        var container = new InputContextContainer();

        var found = container.TryGetContext("doesNotExist", out var context);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(context, Is.Null);
        });
    }

    [Test]
    public void TryGetContext_NullName_ThrowsArgumentNullException()
    {
        var container = new InputContextContainer();

        Assert.Throws<ArgumentNullException>(() => container.TryGetContext(null!, out _));
    }

    #endregion

    #region Remove

    [Test]
    public void Remove_ExistingContext_UnregistersIt()
    {
        var container = new InputContextContainer();
        container.New("child", "common");

        container.Remove("child");

        Assert.Multiple(() =>
        {
            Assert.That(container.Exists("child"), Is.False);
            Assert.Throws<KeyNotFoundException>(() => container.GetContext("child"));
            Assert.That(container.TryGetContext("child", out _), Is.False);
        });
    }

    [Test]
    public void Remove_Common_ThrowsArgumentException()
    {
        var container = new InputContextContainer();

        var ex = Assert.Throws<ArgumentException>(() => container.Remove("common"));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ParamName, Is.EqualTo("uniqueName"));
            Assert.That(container.Exists("common"), Is.True);
        });
    }

    [Test]
    public void Remove_DifferentlyCasedCommon_IsAnUnknownNameAndDoesNothing()
    {
        // Only the exact (ordinal) name "common" is protected. "Common" is simply an unknown name.
        var container = new InputContextContainer();

        Assert.DoesNotThrow(() => container.Remove("Common"));
        Assert.That(container.Exists("common"), Is.True);
    }

    [Test]
    public void Remove_UnknownName_IsNoOp()
    {
        var container = new InputContextContainer();
        container.New("child", "common");

        Assert.DoesNotThrow(() => container.Remove("doesNotExist"));

        Assert.Multiple(() =>
        {
            Assert.That(container.Exists("child"), Is.True);
            Assert.That(container.Exists("common"), Is.True);
        });
    }

    [Test]
    public void Remove_NullName_ThrowsArgumentNullException()
    {
        // The "common" check does not trip for null, so the call reaches the dictionary, which rejects null keys.
        var container = new InputContextContainer();

        Assert.Throws<ArgumentNullException>(() => container.Remove(null!));
    }

    [Test]
    public void Remove_ContextThatIsNotActive_DoesNotChangeActiveContext()
    {
        var container = new InputContextContainer();
        container.New("child", "common");
        var originalActive = container.ActiveContext;
        var events = Record(container);

        container.Remove("child");

        Assert.Multiple(() =>
        {
            Assert.That(container.ActiveContext, Is.SameAs(originalActive));
            Assert.That(events, Is.Empty);
        });
    }

    [Test]
    public void Remove_ActiveContext_LeavesItActiveAndRaisesNoEvent()
    {
        // Removal is allowed even for the active context and does not fall back to "common".
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        container.SetActiveContext("child");
        var events = Record(container);

        container.Remove("child");

        Assert.Multiple(() =>
        {
            Assert.That(container.ActiveContext, Is.SameAs(child));
            Assert.That(events, Is.Empty);
            Assert.That(container.Exists("child"), Is.False);
        });
    }

    [Test]
    public void Remove_ThenNew_ReusesName()
    {
        var container = new InputContextContainer();
        var first = container.New("child", "common");
        container.Remove("child");

        var second = container.New("child", "common");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(container.GetContext("child"), Is.SameAs(second));
        });
    }

    #endregion

    #region SetActiveContext (immediate)

    [Test]
    public void SetActiveContext_ChangesActiveContext()
    {
        var container = new InputContextContainer();
        var child = container.New("child", "common");

        container.SetActiveContext("child");

        Assert.That(container.ActiveContext, Is.SameAs(child));
    }

    [Test]
    public void SetActiveContext_RaisesContextChangedOnceWithOldAndNewContexts()
    {
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        var child = container.New("child", "common");
        var events = Record(container);

        container.SetActiveContext("child");

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(events[0].Sender, Is.SameAs(container));
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(child));
        });
    }

    [Test]
    public void SetActiveContext_ActiveContextIsAlreadyUpdatedWhenHandlersRun()
    {
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        IInputCmdContext? activeSeenByHandler = null;
        container.ContextChanged += (_, _) => activeSeenByHandler = container.ActiveContext;

        container.SetActiveContext("child");

        Assert.That(activeSeenByHandler, Is.SameAs(child));
    }

    [Test]
    public void SetActiveContext_MultipleSwitches_ReportEachTransition()
    {
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        var a = container.New("a", "common");
        var b = container.New("b", "common");
        var events = Record(container);

        container.SetActiveContext("a");
        container.SetActiveContext("b");
        container.SetActiveContext("common");

        // Contexts are compared by reference. NUnit's equality would treat two contexts as equal when they hold
        // the same functions (they are enumerable), which would make the check meaningless here.
        Assert.That(events, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(a));
            Assert.That(events[1].OldContext, Is.SameAs(a));
            Assert.That(events[1].NewContext, Is.SameAs(b));
            Assert.That(events[2].OldContext, Is.SameAs(b));
            Assert.That(events[2].NewContext, Is.SameAs(common));
        });
    }

    [Test]
    public void SetActiveContext_ToAlreadyActiveContext_StillRaisesEvent()
    {
        // There is no short-circuit for "nothing changed": the event fires with old and new being the same instance.
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        var events = Record(container);

        container.SetActiveContext("common");

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(common));
        });
    }

    [Test]
    public void SetActiveContext_UnknownName_ThrowsKeyNotFoundExceptionAndChangesNothing()
    {
        var container = new InputContextContainer();
        var originalActive = container.ActiveContext;
        var events = Record(container);

        Assert.Throws<KeyNotFoundException>(() => container.SetActiveContext("doesNotExist"));

        Assert.Multiple(() =>
        {
            Assert.That(container.ActiveContext, Is.SameAs(originalActive));
            Assert.That(events, Is.Empty);
        });
    }

    [Test]
    public void SetActiveContext_NullName_ThrowsArgumentNullException()
    {
        var container = new InputContextContainer();

        Assert.Throws<ArgumentNullException>(() => container.SetActiveContext(null!));
    }

    [Test]
    public void SetActiveContext_MultipleSubscribers_AllAreNotified()
    {
        var container = new InputContextContainer();
        container.New("child", "common");
        var first = Record(container);
        var second = Record(container);

        container.SetActiveContext("child");

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(second, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SetActiveContext_WithoutSubscribers_DoesNotThrow()
    {
        var container = new InputContextContainer();
        container.New("child", "common");

        Assert.DoesNotThrow(() => container.SetActiveContext("child"));
    }

    [Test]
    public void SetActiveContext_HandlerThatSwitchesAgain_ProducesNestedSwitch()
    {
        // The active context is assigned before the event is raised, so a handler that switches again sees the
        // new context as "old" in the nested event. With deferring disabled the nested switch is immediate.
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        var a = container.New("a", "common");
        var b = container.New("b", "common");
        var events = new List<(IInputCmdContext? OldContext, IInputCmdContext? NewContext)>();
        container.ContextChanged += (_, args) =>
        {
            events.Add((args.OldContext, args.NewContext));
            if (ReferenceEquals(args.NewContext, a))
                container.SetActiveContext("b");
        };

        container.SetActiveContext("a");

        Assert.Multiple(() =>
        {
            // Contexts are compared by reference (see SetActiveContext_MultipleSwitches_ReportEachTransition).
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(a));
            Assert.That(events[1].OldContext, Is.SameAs(a));
            Assert.That(events[1].NewContext, Is.SameAs(b));
            Assert.That(container.ActiveContext, Is.SameAs(b));
        });
    }

    #endregion

    #region Deferred switching

    [Test]
    public void DeferringEnabled_CanBeToggled()
    {
        var container = new InputContextContainer();

        container.DeferringEnabled = true;
        Assert.That(container.DeferringEnabled, Is.True);

        container.DeferringEnabled = false;
        Assert.That(container.DeferringEnabled, Is.False);
    }

    [Test]
    public void DeferringEnabled_Enabling_RaisesNoEvent()
    {
        var container = new InputContextContainer();
        var events = Record(container);

        container.DeferringEnabled = true;

        Assert.That(events, Is.Empty);
    }

    [Test]
    public void SetActiveContext_WhileDeferring_RecordsSwitchWithoutEventOrChange()
    {
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        container.New("child", "common");
        var events = Record(container);
        container.DeferringEnabled = true;

        container.SetActiveContext("child");

        Assert.Multiple(() =>
        {
            Assert.That(container.ActiveContext, Is.SameAs(common));
            Assert.That(events, Is.Empty);
        });
    }

    [Test]
    public void DisablingDeferring_AppliesPendingSwitchAndRaisesEvent()
    {
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        var child = container.New("child", "common");
        var events = Record(container);
        container.DeferringEnabled = true;
        container.SetActiveContext("child");

        container.DeferringEnabled = false;

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(container.ActiveContext, Is.SameAs(child));
            Assert.That(events[0].Sender, Is.SameAs(container));
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(child));
        });
    }

    [Test]
    public void DisablingDeferring_OnlyLastRequestedSwitchIsApplied()
    {
        // There is no queue: the second request overwrites the first, which never raises an event.
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        container.New("a", "common");
        var b = container.New("b", "common");
        var events = Record(container);
        container.DeferringEnabled = true;
        container.SetActiveContext("a");
        container.SetActiveContext("b");

        container.DeferringEnabled = false;

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(b));
            Assert.That(container.ActiveContext, Is.SameAs(b));
        });
    }

    [Test]
    public void DisablingDeferring_LastRequestWinsEvenWhenItReturnsToOriginalContext()
    {
        // Requests: a, then common. Only "common" survives, so the flush performs a switch from common to common.
        var container = new InputContextContainer();
        var common = container.GetContext("common");
        container.New("a", "common");
        var events = Record(container);
        container.DeferringEnabled = true;
        container.SetActiveContext("a");
        container.SetActiveContext("common");

        container.DeferringEnabled = false;

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(events[0].OldContext, Is.SameAs(common));
            Assert.That(events[0].NewContext, Is.SameAs(common));
        });
    }

    [Test]
    public void DisablingDeferring_WithNothingPending_RaisesNoEvent()
    {
        var container = new InputContextContainer();
        var originalActive = container.ActiveContext;
        var events = Record(container);
        container.DeferringEnabled = true;

        container.DeferringEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Empty);
            Assert.That(container.ActiveContext, Is.SameAs(originalActive));
        });
    }

    [Test]
    public void DisablingDeferring_WhenAlreadyDisabled_RaisesNoEvent()
    {
        var container = new InputContextContainer();
        var events = Record(container);

        container.DeferringEnabled = false;

        Assert.That(events, Is.Empty);
    }

    [Test]
    public void DisablingDeferring_Twice_AppliesPendingSwitchOnlyOnce()
    {
        // The pending slot is cleared when the switch is applied, so a second "false" has nothing left to apply.
        var container = new InputContextContainer();
        container.New("child", "common");
        var events = Record(container);
        container.DeferringEnabled = true;
        container.SetActiveContext("child");

        container.DeferringEnabled = false;
        container.DeferringEnabled = false;

        Assert.That(events, Has.Count.EqualTo(1));
    }

    [Test]
    public void DisablingDeferring_AfterApplying_FurtherSwitchesAreImmediate()
    {
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        container.DeferringEnabled = true;
        container.DeferringEnabled = false;
        var events = Record(container);

        container.SetActiveContext("child");

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(container.ActiveContext, Is.SameAs(child));
        });
    }

    [Test]
    public void SetActiveContext_UnknownNameWhileDeferring_ThrowsImmediately()
    {
        // The name is resolved when SetActiveContext is called, not when the switch is applied.
        var container = new InputContextContainer();
        container.DeferringEnabled = true;

        Assert.Throws<KeyNotFoundException>(() => container.SetActiveContext("doesNotExist"));
    }

    [Test]
    public void SetActiveContext_UnknownNameWhileDeferring_KeepsEarlierPendingSwitch()
    {
        // A rejected request must not clear or corrupt a pending switch that was recorded earlier.
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        container.DeferringEnabled = true;
        container.SetActiveContext("child");

        Assert.Throws<KeyNotFoundException>(() => container.SetActiveContext("doesNotExist"));
        container.DeferringEnabled = false;

        Assert.That(container.ActiveContext, Is.SameAs(child));
    }

    [Test]
    public void SetActiveContext_NullNameWhileDeferring_ThrowsArgumentNullException()
    {
        var container = new InputContextContainer();
        container.DeferringEnabled = true;

        Assert.Throws<ArgumentNullException>(() => container.SetActiveContext(null!));
    }

    [Test]
    public void DisablingDeferring_PendingTargetWasRemoved_StillAppliesSwitch()
    {
        // The target object is resolved when the switch is requested, so removing its name from the container
        // afterwards does not cancel the pending switch.
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        container.DeferringEnabled = true;
        container.SetActiveContext("child");
        container.Remove("child");

        container.DeferringEnabled = false;

        Assert.That(container.ActiveContext, Is.SameAs(child));
    }

    [Test]
    public void DisablingDeferring_FlagIsAlreadyStoredWhenHandlersRun()
    {
        // The setter stores the new flag value before applying the pending switch, so handlers observe false.
        var container = new InputContextContainer();
        container.New("child", "common");
        bool? flagSeenByHandler = null;
        container.ContextChanged += (_, _) => flagSeenByHandler = container.DeferringEnabled;
        container.DeferringEnabled = true;
        container.SetActiveContext("child");

        container.DeferringEnabled = false;

        Assert.That(flagSeenByHandler, Is.False);
    }

    [Test]
    public void DisablingDeferring_HandlerThatDisablesDeferringAgain_DoesNotApplySwitchTwice()
    {
        // The pending slot is cleared before the switch is performed, so re-entering the setter from a handler
        // finds nothing pending and raises no further event.
        var container = new InputContextContainer();
        container.New("child", "common");
        var eventCount = 0;
        container.ContextChanged += (_, _) =>
        {
            eventCount++;
            container.DeferringEnabled = false;
        };
        container.DeferringEnabled = true;
        container.SetActiveContext("child");

        container.DeferringEnabled = false;

        Assert.That(eventCount, Is.EqualTo(1));
    }

    [Test]
    public void DisablingDeferring_HandlerThrows_ExceptionPropagatesAfterStateWasUpdated()
    {
        // Handler exceptions propagate out of the setter, but by then the flag is stored, the pending slot is
        // cleared and the active context has been switched.
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        container.ContextChanged += (_, _) => throw new InvalidOperationException("handler failure");
        container.DeferringEnabled = true;
        container.SetActiveContext("child");

        Assert.Throws<InvalidOperationException>(() => container.DeferringEnabled = false);

        Assert.Multiple(() =>
        {
            Assert.That(container.DeferringEnabled, Is.False);
            Assert.That(container.ActiveContext, Is.SameAs(child));
        });
    }

    [Test]
    public void SetActiveContext_HandlerThrows_ExceptionPropagatesToCaller()
    {
        var container = new InputContextContainer();
        var child = container.New("child", "common");
        container.ContextChanged += (_, _) => throw new InvalidOperationException("handler failure");

        Assert.Throws<InvalidOperationException>(() => container.SetActiveContext("child"));

        // The active context was assigned before the event was raised, so it is updated despite the exception.
        Assert.That(container.ActiveContext, Is.SameAs(child));
    }

    #endregion

    #region ContextChangedEventArgs

    [Test]
    public void ContextChangedEventArgs_StoresOldAndNewContextInThatOrder()
    {
        var oldContext = new InputCmdContext("old");
        var newContext = new InputCmdContext("new");

        var args = new ContextChangedEventArgs(oldContext, newContext);

        Assert.Multiple(() =>
        {
            Assert.That(args.OldContext, Is.SameAs(oldContext));
            Assert.That(args.NewContext, Is.SameAs(newContext));
        });
    }

    [Test]
    public void ContextChangedEventArgs_AcceptsNullContexts()
    {
        var args = new ContextChangedEventArgs(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(args.OldContext, Is.Null);
            Assert.That(args.NewContext, Is.Null);
        });
    }

    #endregion
}
