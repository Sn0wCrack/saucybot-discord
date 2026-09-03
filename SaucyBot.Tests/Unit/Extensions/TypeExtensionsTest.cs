using System;
using SaucyBot.Extensions;
using Xunit;

namespace SaucyBot.Tests.Unit.Extensions;

public class TypeExtensionsTest
{
    [Fact]
    public void DerivedTypeIsSubclassOfOpenGeneric()
    {
        Assert.True(typeof(Derived).IsSubclassOfOpenGeneric(typeof(Base<>)));
    }

    [Fact]
    public void UnrelatedTypeIsNotSubclassOfOpenGeneric()
    {
        Assert.False(typeof(string).IsSubclassOfOpenGeneric(typeof(Base<>)));
    }

    [Fact]
    public void NonDefinitionArgumentThrows()
    {
        Assert.Throws<ArgumentException>(() => typeof(Derived).IsSubclassOfOpenGeneric(typeof(Base<int>)));
    }

    [Fact]
    public void NullArgumentThrows()
    {
        Assert.Throws<ArgumentNullException>(() => typeof(Derived).IsSubclassOfOpenGeneric(null!));
    }

    private class Base<T>
    {
    }

    private class Derived : Base<int>
    {
    }
}
