using Snail.Toolkit.Template;

var text = Greeting.For("native");

Console.WriteLine(text);

return text == "Hello, native" ? 0 : 1;
