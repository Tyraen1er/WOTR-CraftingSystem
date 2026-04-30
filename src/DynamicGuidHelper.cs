using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker.Blueprints;

namespace CraftingSystem
{
    public static class DynamicGuidHelper
    {
        public const string Signature = "C2AF";

        /// <summary>
        /// Génère un GUID déterministe encodant l'ID de l'enchantement et ses paramètres.
        /// Format : [C2AF (4)] [EnchantId (3)] [Params (variable)] [00...00 (remplissage)]
        /// </summary>
        public static BlueprintGuid GenerateGuid(string enchantId, params int[] parameters)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Signature);
            
            // On s'assure que l'ID fait 3 caractères (ex: "001")
            string id = enchantId ?? "000";
            if (id.Length > 3) id = id.Substring(0, 3);
            else while (id.Length < 3) id = "0" + id;
            
            sb.Append(id);

            // Nombre de paramètres (1 caractère hexa : 0-F)
            int count = parameters?.Length ?? 0;
            sb.Append(count.ToString("X1"));

            // Encodage des paramètres (2 chars hexa par paramètre)
            if (parameters != null)
            {
                foreach (int p in parameters)
                {
                    // On clamp à 0-255 pour tenir sur 2 caractères
                    int clamped = Math.Max(0, Math.Min(255, p));
                    sb.Append(clamped.ToString("X2"));
                }
            }

            // Remplissage avec des zéros pour atteindre 32 caractères
            while (sb.Length < 32)
            {
                sb.Append("0");
            }

            string finalString = sb.ToString();
            if (finalString.Length > 32) finalString = finalString.Substring(0, 32);

            return BlueprintGuid.Parse(finalString);
        }

        /// <summary>
        /// Décode un GUID pour retrouver l'ID de l'enchantement et ses paramètres.
        /// </summary>
        public static bool TryDecodeGuid(BlueprintGuid guid, out string enchantId, out List<int> parameters)
        {
            enchantId = null;
            parameters = new List<int>();
            
            string s = guid.ToString().Replace("-", "").ToUpper();
            
            if (s.Length != 32 || !s.StartsWith(Signature))
                return false;

            enchantId = s.Substring(Signature.Length, 3);
            
            // Lecture du nombre de paramètres
            string countHex = s.Substring(Signature.Length + 3, 1);
            int count = Convert.ToInt32(countHex, 16);

            // On décode exactement 'count' paramètres
            int startIdx = Signature.Length + 3 + 1;
            for (int i = 0; i < count; i++)
            {
                int pos = startIdx + (i * 2);
                if (pos + 1 >= 32) break;

                string hex = s.Substring(pos, 2);
                try {
                    parameters.Add(Convert.ToInt32(hex, 16));
                } catch {
                    break;
                }
            }

            return true;
        }
    }
}
