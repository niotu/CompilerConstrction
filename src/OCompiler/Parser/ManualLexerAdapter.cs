using System.Collections.Generic;
using OCompiler.Lexer;
using QUT.Gppg;


namespace OCompiler.Parser
{
    public class ManualLexerAdapter : AbstractScanner<ValueType, LexLocation>
    {
        private readonly IEnumerator<Token> _tokenStream;

        public ManualLexerAdapter(IEnumerable<Token> tokenStream)
        {
            _tokenStream = tokenStream.GetEnumerator();
        }

        public override int yylex()
        {
            if (!_tokenStream.MoveNext())
                {
                    Console.WriteLine("Lexer is empty (EOF reached)");
                    return (int)Tokens.EOF;
                }

            var current = _tokenStream.Current;

            // По умолчанию очищаем yylval
            yylval = default(ValueType);

            switch (current.Type)
            {
                case TokenType.IDENTIFIER:
                    yylval.str = current.Value;
                    break;
                case TokenType.INTEGER_LITERAL:
                    yylval.integer = int.Parse(current.Value);
                    break;
                case TokenType.REAL_LITERAL:
                    yylval.real = double.Parse(current.Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case TokenType.BOOLEAN_LITERAL:
                    yylval.boolean = bool.Parse(current.Value);
                    break;
                // Для ключевых слов и других токенов yylval не заполняем
            }

            // Возвращаем соответствующий числовой код токена для парсера
            return (int)MapTokenTypeToEnum(current.Type);
        }

        // Тебе нужно определить соответствие TokenType (из Lexer) с enum Tokens (парсер)
        private Tokens MapTokenTypeToEnum(TokenType type)
        {
            switch (type)
            {
                case TokenType.IDENTIFIER: return Tokens.IDENTIFIER;
                case TokenType.INTEGER_LITERAL: return Tokens.INTEGER_LITERAL;
                case TokenType.REAL_LITERAL: return Tokens.REAL_LITERAL;
                case TokenType.BOOLEAN_LITERAL: return Tokens.BOOLEAN_LITERAL;
                case TokenType.CLASS: return Tokens.CLASS;
                case TokenType.ELSE: return Tokens.ELSE;
                case TokenType.END: return Tokens.END;
                case TokenType.EXTENDS: return Tokens.EXTENDS;
                case TokenType.IF: return Tokens.IF;
                case TokenType.IS: return Tokens.IS;
                case TokenType.LOOP: return Tokens.LOOP;
                case TokenType.METHOD: return Tokens.METHOD;
                case TokenType.RETURN: return Tokens.RETURN;
                case TokenType.THEN: return Tokens.THEN;
                case TokenType.THIS: return Tokens.THIS;
                case TokenType.VAR: return Tokens.VAR;
                case TokenType.WHILE: return Tokens.WHILE;
                case TokenType.ASSIGN: return Tokens.ASSIGN;
                case TokenType.ARROW: return Tokens.ARROW;
                case TokenType.COLON: return Tokens.COLON;
                case TokenType.DOT: return Tokens.DOT;
                case TokenType.COMMA: return Tokens.COMMA;
                case TokenType.LPAREN: return Tokens.LPAREN;
                case TokenType.RPAREN: return Tokens.RPAREN;
                case TokenType.LBRACKET: return Tokens.LBRACKET;
                case TokenType.RBRACKET: return Tokens.RBRACKET;
                case TokenType.UNKNOWN: return Tokens.UNKNOWN;
                case TokenType.COMMENT: return Tokens.COMMENT;
                case TokenType.EOF: return Tokens.EOF;
                default:
                    return Tokens.UNKNOWN;
            }
        }
    }
}