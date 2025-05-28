using AngleSharp.Css.Dom;

namespace Lite.Models;

public record DrawCommand(string? Id, string TagName, string Text, ICssStyleDeclaration CssStyleDeclaration);