namespace Snail.Toolkit.Template.Tests;

public sealed class GreetingTests
{
    [Fact]
    public void For_AName_SaysHelloToIt() => Assert.Equal("Hello, world", Greeting.For("world"));

    [Fact]
    public void For_NoName_IsRefused() => Assert.Throws<ArgumentException>(() => Greeting.For(" "));
}
