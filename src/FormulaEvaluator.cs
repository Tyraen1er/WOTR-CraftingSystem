using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CraftingSystem
{
    public static class FormulaEvaluator
    {
        /// <summary>
        /// Évalue une expression mathématique simple en remplaçant les variables par leurs valeurs.
        /// Supporte : +, -, *, /, (, )
        /// </summary>
        public static double Evaluate(string formula, Dictionary<string, double> variables)
        {
            if (string.IsNullOrEmpty(formula)) return 0;

            string expression = formula;

            // 1. Remplacement des variables
            // On trie les variables par longueur décroissante pour éviter que "Val" ne remplace le début de "Value"
            var sortedVars = variables.Keys.OrderByDescending(k => k.Length);
            foreach (var varName in sortedVars)
            {
                // On utilise Regex pour s'assurer qu'on remplace le mot entier (\b)
                expression = Regex.Replace(expression, @"\b" + varName + @"\b", variables[varName].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            try
            {
                // Nettoyage pour le parser simple
                expression = expression.Replace(" ", "");
                double result = ParseExpression(expression);
                Main.ModEntry.Logger.Log($"[FORMULA] Evaluated '{formula}' -> '{expression}' = {result}");
                return result;
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[FORMULA] Error evaluating formula '{formula}' (processed as '{expression}'): {ex.Message}");
                return 0;
            }
        }

        public static int EvaluateInt(string formula, Dictionary<string, double> variables)
        {
            return (int)Math.Round(Evaluate(formula, variables));
        }

        public static long EvaluateLong(string formula, Dictionary<string, double> variables)
        {
            return (long)Math.Round(Evaluate(formula, variables));
        }

        // --- PARSER MANUEL SIMPLE (Recursive Descent) ---
        // Supporte +, -, *, /, ()
        private static double ParseExpression(string expression)
        {
            int pos = -1;
            int ch = 0;

            void NextChar()
            {
                ch = (++pos < expression.Length) ? expression[pos] : -1;
            }

            bool Eat(int charToEat)
            {
                while (ch == ' ') NextChar();
                if (ch == charToEat)
                {
                    NextChar();
                    return true;
                }
                return false;
            }

            double ParseFactor()
            {
                if (Eat('+')) return ParseFactor(); // unary plus
                if (Eat('-')) return -ParseFactor(); // unary minus

                double x;
                int startPos = pos;
                if (Eat('('))
                { // parentheses
                    x = ParseExpressionInternal();
                    Eat(')');
                }
                else if ((ch >= '0' && ch <= '9') || ch == '.')
                { // numbers
                    while ((ch >= '0' && ch <= '9') || ch == '.') NextChar();
                    x = double.Parse(expression.Substring(startPos, pos - startPos), System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    throw new Exception("Unexpected: " + (char)ch);
                }

                return x;
            }

            double ParseTerm()
            {
                double x = ParseFactor();
                for (; ; )
                {
                    if (Eat('*')) x *= ParseFactor(); // multiplication
                    else if (Eat('/')) x /= ParseFactor(); // division
                    else return x;
                }
            }

            double ParseExpressionInternal()
            {
                double x = ParseTerm();
                for (; ; )
                {
                    if (Eat('+')) x += ParseTerm(); // addition
                    else if (Eat('-')) x -= ParseTerm(); // subtraction
                    else return x;
                }
            }

            NextChar();
            return ParseExpressionInternal();
        }
    }
}
