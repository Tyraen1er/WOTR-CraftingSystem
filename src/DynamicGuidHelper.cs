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
        public static BlueprintGuid GenerateGuid(string enchantId, int[] parameters, bool isFeature = false)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Signature);
            
            // On s'assure que l'ID fait 3 caractères (ex: "001")
            string id = enchantId ?? "000";
            if (id.Length > 3) id = id.Substring(0, 3);
            else while (id.Length < 3) id = "0" + id;
            
            sb.Append(id);

            // On prépare la liste finale (le flag isFeature est le PREMIER paramètre)
            List<int> finalParams = new List<int>();
            finalParams.Add(isFeature ? 1 : 0);
            if (parameters != null) finalParams.AddRange(parameters);

            // Nombre de paramètres (1 caractère hexa : 0-F)
            int count = finalParams.Count;
            sb.Append(count.ToString("X1"));

            // Encodage des paramètres (2 chars hexa par paramètre)
            foreach (int p in finalParams)
            {
                // On clamp à 0-255 pour tenir sur 2 caractères
                int clamped = Math.Max(0, Math.Min(255, p));
                sb.Append(clamped.ToString("X2"));
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
        /// Génère le GUID par défaut d'un modèle (tous paramètres à 0).
        /// </summary>
        public static BlueprintGuid GenerateModelGuid(string enchantId, bool isFeature = false)
        {
            return GenerateGuid(enchantId, new int[0], isFeature);
        }

        /// <summary>
        /// Décode un GUID pour retrouver l'ID de l'enchantement et ses paramètres.
        /// </summary>
        public static bool TryDecodeGuid(BlueprintGuid guid, out string enchantId, out List<int> parameters)
        {
            enchantId = null;
            parameters = new List<int>();

            // Optimisation : On vérifie les premiers octets avant de faire un ToString coûteux
            // Le GUID est stocké en Little Endian ou Big Endian selon les plateformes,
            // mais BlueprintGuid.ToByteArray() nous donne une base stable.
            // "C2AF" en hexa (Big Endian) correspondrait aux 2 premiers octets.
            // En chaîne "C2AF...", C2 est l'octet 0, AF l'octet 1.
            
            byte[] bytes = guid.ToByteArray();
            // On vérifie si la chaîne commencerait par "C2AF"
            // Dans un GUID .NET, les 4 premiers octets sont inversés (int), les 2 suivants (short), etc.
            // "C2AF0012-..." -> Data1 = 0xC2AF0012
            // Sur architecture Little Endian, les octets de Data1 sont [12, 00, AF, C2]
            
            if (bytes[3] != 0xC2 || bytes[2] != 0xAF)
                return false;

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
