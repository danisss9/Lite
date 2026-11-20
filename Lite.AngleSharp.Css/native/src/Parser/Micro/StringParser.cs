#nullable disable
namespace AngleSharp.Css.Parser
{
    using AngleSharp.Text;
    using System;

    /// <summary>
    /// Represents extensions to for string values.
    /// </summary>
    public static class StringParser
    {
        /// <summary>
        /// Parses a string object.
        /// https://developer.mozilla.org/en-US/docs/Web/CSS/string
        /// </summary>
        public static String ParseString(this StringSource source)
        {
            var current = source.Current;

            return current switch
            {
                Symbols.DoubleQuote => DoubleQuoted(source),
                Symbols.SingleQuote => SingleQuoted(source),
                _ => null,
            };
        }

        private static String DoubleQuoted(StringSource source)
        {
            var buffer = StringBuilderPool.Obtain();

            while (true)
            {
                var current = source.Next();

                switch (current)
                {
                    case Symbols.DoubleQuote:
                    case Symbols.EndOfFile:
                        source.Next();
                        return buffer.ToPool();

                    case Symbols.FormFeed:
                    case Symbols.LineFeed:
                        buffer.ToPool();
                        return null;

                    case Symbols.ReverseSolidus:
                        current = source.Next();

                        if (current.IsLineBreak())
                        {
                            // Lite patch 008: CSS 2.1 4.3.7 - an escaped newline is removed from the string.
                        }
                        else if (current != Symbols.EndOfFile)
                        {
                            source.Back();
                            buffer.Append(source.ConsumeEscape());
                        }
                        else
                        {
                            return buffer.ToPool();
                        }

                        break;

                    default:
                        buffer.Append(current);
                        break;
                }
            }
        }

        private static String SingleQuoted(StringSource source)
        {
            var buffer = StringBuilderPool.Obtain();

            while (true)
            {
                var current = source.Next();

                switch (current)
                {
                    case Symbols.SingleQuote:
                    case Symbols.EndOfFile:
                        source.Next();
                        return buffer.ToPool();

                    case Symbols.FormFeed:
                    case Symbols.LineFeed:
                        buffer.ToPool();
                        return null;

                    case Symbols.ReverseSolidus:
                        current = source.Next();

                        if (current.IsLineBreak())
                        {
                            // Lite patch 008: CSS 2.1 4.3.7 - an escaped newline is removed from the string.
                        }
                        else if (current != Symbols.EndOfFile)
                        {
                            source.Back();
                            buffer.Append(source.ConsumeEscape());
                        }
                        else
                        {
                            return buffer.ToPool();
                        }

                        break;

                    default:
                        buffer.Append(current);
                        break;
                }
            }
        }
    }
}
