using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CraftingSystem
{
    public static class FormulaEvaluator
    {
        private static readonly HashSet<string> SupportedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MAX", "MIN", "ABS", "FLOOR", "CEIL", "ROUND" };
 
        /// <summary>
        /// Évalue une expression mathématique simple en remplaçant les variables par leurs valeurs.
        /// Supporte : +, -, *, /, (, )
        /// </summary>
        public static double Evaluate(string formula, Dictionary<string, double> variables)
        {
            if (string.IsNullOrEmpty(formula)) return 0;

            string expression = formula;
            var missingVars = new List<string>();

            // 1. Remplacement des variables
            var sortedVars = variables.Keys.OrderByDescending(k => k.Length).ToList();
            
            // On vérifie d'abord si toutes les variables potentielles de la formule sont présentes
            // Pour être simple, on check juste le texte de la formule
            // Mais le remplacement par Regex \b est déjà une bonne sécurité.

            foreach (var varName in sortedVars)
            {
                string escapedVarName = Regex.Escape(varName);
                // On utilise un lookbehind/lookahead pour s'assurer qu'on ne remplace pas un morceau de variable
                // mais on autorise les points et underscores à l'intérieur.
                string pattern = @"(?<![a-zA-Z0-9_\.])" + escapedVarName + @"(?![a-zA-Z0-9_\.])";
                if (Regex.IsMatch(expression, pattern))
                {
                    expression = Regex.Replace(expression, pattern, variables[varName].ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            // 2. Prétraitement des fonctions (MAX, MIN, ABS, etc.)
            // On le fait AVANT la vérification des variables manquantes pour que MAX(...) disparaisse
            try {
                expression = expression.Replace(" ", "");
                expression = ProcessFunctions(expression);
            } catch { /* On laisse la vérification suivante attraper les erreurs */ }
 
            // 3. Vérification après remplacement : reste-t-il des patterns alphabétiques non résolus ?
            var matches = Regex.Matches(expression, @"[a-zA-Z_][a-zA-Z0-9_\.]*");
            foreach (Match m in matches)
            {
                if (SupportedFunctions.Contains(m.Value)) continue;
                if (!double.TryParse(m.Value, out _)) missingVars.Add(m.Value);
            }
 
            if (missingVars.Count > 0)
            {
                Main.ModEntry.Logger.Error($"[FORMULA] Missing variables in formula '{formula}': {string.Join(", ", missingVars)}");
                return double.NaN;
            }

            try
            {
                double result = ParseExpression(expression);
                
                if (double.IsInfinity(result) || double.IsNaN(result))
                {
                    Main.ModEntry.Logger.Error($"[FORMULA] Calculation resulted in non-finite value: '{expression}' = {result}");
                    return double.NaN;
                }

                return result;
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[FORMULA] Error evaluating formula '{formula}' (processed as '{expression}'): {ex.Message}");
                return double.NaN;
            }
        }

        private static string ProcessFunctions(string expr)
        {
            expr = HandleFunction(expr, "MAX", args => args.Max());
            expr = HandleFunction(expr, "MIN", args => args.Min());
            expr = HandleFunction(expr, "ABS", args => Math.Abs(args[0]));
            expr = HandleFunction(expr, "FLOOR", args => Math.Floor(args[0]));
            expr = HandleFunction(expr, "CEIL", args => Math.Ceiling(args[0]));
            expr = HandleFunction(expr, "ROUND", args => Math.Round(args[0]));
            return expr;
        }

        private static string HandleFunction(string expr, string funcName, Func<List<double>, double> logic)
        {
            string pattern = funcName + @"\(([^()]+)\)";
            while (Regex.IsMatch(expr, pattern))
            {
                expr = Regex.Replace(expr, pattern, m =>
                {
                    // On parse chaque argument séparément (peut être une sous-formule sans virgule)
                    var args = m.Groups[1].Value.Split(',')
                                .Select(s => ParseExpression(s.Trim()))
                                .ToList();
                    return logic(args).ToString(System.Globalization.CultureInfo.InvariantCulture);
                });
            }
            return expr;
        }

        public static int EvaluateInt(string formula, Dictionary<string, double> variables)
        {
            double res = Evaluate(formula, variables);
            if (double.IsNaN(res) || double.IsInfinity(res)) return 0;
            if (res > int.MaxValue) return int.MaxValue;
            if (res < int.MinValue) return int.MinValue;
            return (int)Math.Round(res);
        }

        public static long EvaluateLong(string formula, Dictionary<string, double> variables)
        {
            double res = Evaluate(formula, variables);
            if (double.IsNaN(res) || double.IsInfinity(res)) return 0;
            if (res > long.MaxValue) return long.MaxValue;
            if (res < long.MinValue) return long.MinValue;
            return (long)Math.Round(res);
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
