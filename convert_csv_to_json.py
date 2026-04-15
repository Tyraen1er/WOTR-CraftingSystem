import csv
import json
import os
import sys

def convert_csv_to_json(csv_filepath):
    """
    Convertit un fichier CSV en JSON. 
    Affiche des logs uniquement en cas d'erreur.
    """
    if not os.path.exists(csv_filepath):
        print(f"Erreur : Le fichier d'entrée '{csv_filepath}' n'existe pas.", file=sys.stderr)
        return False

    # Génération du nom de fichier de sortie (.json)
    json_filepath = os.path.splitext(csv_filepath)[0] + '.json'
    data = []

    # Listes de colonnes à traiter (en minuscules pour ignorer la casse des en-têtes)
    bool_columns_lower = ['isepic', 'ishomebrew']
    int_columns_lower = ['pricefactor', 'priceoverride']

    try:
        with open(csv_filepath, mode='r', encoding='utf-8', newline='') as csvfile:
            reader = csv.DictReader(csvfile, delimiter=',')
            
            for row in reader:
                # On parcourt toutes les clés pour appliquer les conversions dynamiquement
                for key in list(row.keys()):
                    if not key: # Sécurité si une colonne n'a pas de nom
                        continue
                        
                    key_lower = key.lower()

                    # 1. Traitement strict des booléens
                    if key_lower in bool_columns_lower:
                        # Si c'est 'true' (peu importe la casse), on met True. Sinon, False.
                        row[key] = str(row[key]).strip().lower() == 'true'
                    
                    # 2. Traitement strict des entiers
                    elif key_lower in int_columns_lower:
                        val = str(row[key]).strip()
                        if val == '':
                            row[key] = -1
                        else:
                            try:
                                row[key] = int(val)
                            except ValueError:
                                row[key] = -1 # Sécurité anti-crash si du texte est tapé par erreur
                                
                # 3. Traitement spécial pour le champ Categories (conversion en tableau)
                # On cherche la vraie clé peu importe sa casse (Categories, categories, CATEGORIES...)
                cat_key = next((k for k in row.keys() if k and k.lower() == 'categories'), None)
                if cat_key:
                    categories_str = str(row[cat_key]).strip()
                    if categories_str:
                        row[cat_key] = [cat.strip() for cat in categories_str.split(',') if cat.strip()]
                    else:
                        row[cat_key] = []
                else:
                    # Si la colonne manque totalement dans le CSV, on l'ajoute par sécurité
                    row['Categories'] = []
                
                data.append(row)

        with open(json_filepath, mode='w', encoding='utf-8') as jsonfile:
            # dump génère le fichier json, ensure_ascii=False préserve les accents (é, à...)
            json.dump(data, jsonfile, indent=4, ensure_ascii=False)
        
        return json_filepath

    except Exception as e:
        print(f"Une erreur est survenue lors de la conversion : {e}", file=sys.stderr)
        return False

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python convert_csv_to_json.py <nom_du_fichier.csv>")
        sys.exit(1)

    input_csv = sys.argv[1]
    
    result = convert_csv_to_json(input_csv)
    if result:
        print(f"Le processus s'est terminé avec succès. Fichier généré : {result}")