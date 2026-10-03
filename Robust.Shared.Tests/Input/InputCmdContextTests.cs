using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Robust.Shared.Input;

namespace Robust.Shared.Tests.Input;

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(InputCmdContext))]
internal sealed class InputCmdContextTests
{
    // BoundKeyFunction converts implicitly from string, so plain string literals are used for functions below.
    private static readonly BoundKeyFunction FuncA = "FuncA";
    private static readonly BoundKeyFunction FuncB = "FuncB";
    private static readonly BoundKeyFunction FuncC = "FuncC";

    [Test]
    public void Name_IsStoredExactlyAsGiven()
    {
        // The context does not normalise names (no trimming, no case folding); validation is the job of
        // InputContextContainer, which is the only caller that creates contexts in production.
        var context = new InputCmdContext("MyContext");

        Assert.That(context.Name, Is.EqualTo("MyContext"));
    }

    [Test]
    public void Name_IsStoredWithParentConstructor()
    {
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");

        Assert.Multiple(() =>
        {
            Assert.That(parent.Name, Is.EqualTo("parent"));
            Assert.That(child.Name, Is.EqualTo("child"));
        });
    }

    [Test]
    public void Constructor_DoesNotValidateName()
    {
        // The class deliberately performs no argument validation, so an empty name must be accepted
        // rather than rejected with an exception.
        InputCmdContext? context = null;

        Assert.DoesNotThrow(() => context = new InputCmdContext(string.Empty));

        Assert.That(context!.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void NewContext_IsEmpty()
    {
        var context = new InputCmdContext("ctx");

        Assert.That(context.ToArray(), Is.Empty);
    }

    [Test]
    public void FunctionExists_AfterAdd_IsTrue()
    {
        var context = new InputCmdContext("ctx");

        context.AddFunction(FuncA);

        Assert.That(context.FunctionExists(FuncA), Is.True);
    }

    [Test]
    public void FunctionExists_UnknownFunction_IsFalse()
    {
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);

        Assert.That(context.FunctionExists(FuncB), Is.False);
    }

    [Test]
    public void FunctionExists_ComparesNamesCaseSensitively()
    {
        // Functions are compared with the ordinal equality of BoundKeyFunction, so a name differing only
        // by case is a different function.
        var context = new InputCmdContext("ctx");
        context.AddFunction("Use");

        Assert.Multiple(() =>
        {
            Assert.That(context.FunctionExists("Use"), Is.True);
            Assert.That(context.FunctionExists("use"), Is.False);
        });
    }

    [Test]
    public void FunctionExists_IgnoresParentChain()
    {
        // FunctionExists is strictly local: a function that only the parent knows about must not be reported.
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");
        parent.AddFunction(FuncA);

        Assert.That(child.FunctionExists(FuncA), Is.False);
    }

    [Test]
    public void FunctionExistsHierarchy_FindsFunctionInOwnContext()
    {
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");
        child.AddFunction(FuncA);

        Assert.That(child.FunctionExistsHierarchy(FuncA), Is.True);
    }

    [Test]
    public void FunctionExistsHierarchy_FindsFunctionInGrandparent()
    {
        // Chain: grandparent <- parent <- child. The function lives only in the grandparent, so the lookup
        // has to walk through more than one level of ancestors.
        var grandparent = new InputCmdContext("grandparent");
        var parent = new InputCmdContext(grandparent, "parent");
        var child = new InputCmdContext(parent, "child");
        grandparent.AddFunction(FuncA);

        Assert.Multiple(() =>
        {
            Assert.That(child.FunctionExistsHierarchy(FuncA), Is.True);
            Assert.That(parent.FunctionExistsHierarchy(FuncA), Is.True);
            Assert.That(grandparent.FunctionExistsHierarchy(FuncA), Is.True);

            // The non-hierarchical check still only looks at the context itself.
            Assert.That(child.FunctionExists(FuncA), Is.False);
            Assert.That(parent.FunctionExists(FuncA), Is.False);
            Assert.That(grandparent.FunctionExists(FuncA), Is.True);
        });
    }

    [Test]
    public void FunctionExistsHierarchy_UnknownFunction_IsFalse()
    {
        var grandparent = new InputCmdContext("grandparent");
        var parent = new InputCmdContext(grandparent, "parent");
        var child = new InputCmdContext(parent, "child");
        grandparent.AddFunction(FuncA);
        parent.AddFunction(FuncB);

        Assert.That(child.FunctionExistsHierarchy(FuncC), Is.False);
    }

    [Test]
    public void FunctionExistsHierarchy_DoesNotConsultChildren()
    {
        // Only ancestors are searched. A function registered on a child must not be visible from its parent.
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");
        child.AddFunction(FuncA);

        Assert.That(parent.FunctionExistsHierarchy(FuncA), Is.False);
    }

    [Test]
    public void SingleArgumentConstructor_CreatesContextWithoutParent()
    {
        // Without a parent the hierarchy lookup has nowhere to go after the local list, so it must simply
        // return false instead of failing on a missing parent.
        var context = new InputCmdContext("root");
        context.AddFunction(FuncA);

        Assert.Multiple(() =>
        {
            Assert.That(context.FunctionExistsHierarchy(FuncA), Is.True);
            Assert.That(context.FunctionExistsHierarchy(FuncB), Is.False);
        });
    }

    [Test]
    public void TwoArgumentConstructor_WithNullParent_BehavesLikeNoParent()
    {
        var context = new InputCmdContext(null, "root");
        context.AddFunction(FuncA);

        Assert.Multiple(() =>
        {
            Assert.That(context.FunctionExistsHierarchy(FuncA), Is.True);
            Assert.That(context.FunctionExistsHierarchy(FuncB), Is.False);
        });
    }

    [Test]
    public void RemoveFunction_RemovesExistingFunction()
    {
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);

        context.RemoveFunction(FuncA);

        Assert.That(context.FunctionExists(FuncA), Is.False);
    }

    [Test]
    public void RemoveFunction_WhenAbsent_DoesNothing()
    {
        // Removing a function that was never added must neither throw nor disturb the existing entries.
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);

        Assert.DoesNotThrow(() => context.RemoveFunction(FuncB));

        Assert.That(context.ToArray(), Is.EqualTo(new[] { FuncA }));
    }

    [Test]
    public void RemoveFunction_AddedTwice_RemovesOnlyOneOccurrence()
    {
        // The context stores functions in a list, not a set, so duplicates are allowed and each call to
        // RemoveFunction removes just one of them.
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);
        context.AddFunction(FuncA);

        context.RemoveFunction(FuncA);
        Assert.That(context.FunctionExists(FuncA), Is.True, "One copy should remain after the first removal.");

        context.RemoveFunction(FuncA);
        Assert.That(context.FunctionExists(FuncA), Is.False, "The second removal should remove the last copy.");
    }

    [Test]
    public void RemoveFunction_DoesNotAffectParent()
    {
        // Removing from a child only touches the child's own list. The parent keeps its function, so the
        // hierarchy lookup from the child still finds it there.
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");
        parent.AddFunction(FuncA);
        child.AddFunction(FuncA);

        child.RemoveFunction(FuncA);

        Assert.Multiple(() =>
        {
            Assert.That(child.FunctionExists(FuncA), Is.False);
            Assert.That(parent.FunctionExists(FuncA), Is.True);
            Assert.That(child.FunctionExistsHierarchy(FuncA), Is.True);
        });
    }

    [Test]
    public void AddFunction_DoesNotAffectParent()
    {
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");

        child.AddFunction(FuncA);

        Assert.That(parent.FunctionExists(FuncA), Is.False);
    }

    [Test]
    public void Enumeration_YieldsOwnFunctionsInInsertionOrder()
    {
        // The functions are deliberately not added in alphabetical order, to show that enumeration follows
        // insertion order rather than any sorting.
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncC);
        context.AddFunction(FuncA);
        context.AddFunction(FuncB);

        Assert.That(context.ToArray(), Is.EqualTo(new[] { FuncC, FuncA, FuncB }));
    }

    [Test]
    public void Enumeration_IncludesDuplicates()
    {
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);
        context.AddFunction(FuncB);
        context.AddFunction(FuncA);

        Assert.That(context.ToArray(), Is.EqualTo(new[] { FuncA, FuncB, FuncA }));
    }

    [Test]
    public void Enumeration_ExcludesParentFunctions()
    {
        // Enumeration reports only what was registered directly on the context itself.
        var parent = new InputCmdContext("parent");
        var child = new InputCmdContext(parent, "child");
        parent.AddFunction(FuncA);
        child.AddFunction(FuncB);

        Assert.Multiple(() =>
        {
            Assert.That(child.ToArray(), Is.EqualTo(new[] { FuncB }));
            Assert.That(parent.ToArray(), Is.EqualTo(new[] { FuncA }));
        });
    }

    [Test]
    public void Enumeration_NonGenericEnumeratorYieldsSameElements()
    {
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);
        context.AddFunction(FuncB);

        var nonGeneric = (System.Collections.IEnumerable) context;
        var items = new List<object?>();
        foreach (var item in nonGeneric)
        {
            items.Add(item);
        }

        Assert.That(items, Is.EqualTo(new object[] { FuncA, FuncB }));
    }

    [Test]
    public void Enumeration_AddingDuringEnumeration_Throws()
    {
        // The enumerator is live rather than a snapshot, so modifying the context while iterating it
        // invalidates the enumerator. This is intended behaviour that callers rely on.
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);
        context.AddFunction(FuncB);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in context)
            {
                context.AddFunction(FuncC);
            }
        });
    }

    [Test]
    public void Enumeration_RemovingDuringEnumeration_Throws()
    {
        var context = new InputCmdContext("ctx");
        context.AddFunction(FuncA);
        context.AddFunction(FuncB);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var function in context)
            {
                context.RemoveFunction(function);
            }
        });
    }
}
