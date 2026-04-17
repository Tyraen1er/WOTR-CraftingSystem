import json
import os
import re

def simplify_value(val):
    if isinstance(val, str):
        # Match "ContextValue [Simple]: 10" -> 10
        match = re.match(r"ContextValue \[Simple\]: (-?\d+)", val)
        if match:
            return int(match.group(1))
        
        # Match "Acid (4)" -> "Acid"
        # Optional: keep the string clean if it matches Enum format
        enum_match = re.match(r"(.+) \(\d+\)", val)
        if enum_match:
            return enum_match.group(1)
            
    if isinstance(val, dict):
        return clean_dict(val)
    if isinstance(val, list):
        return [simplify_value(v) for v in val]
    return val

def clean_dict(d):
    new_dict = {}
    
    # Check for UsePool logic
    use_pool = d.get("UsePool") == True
    
    for k, v in d.items():
        # Garbage fields
        if k in ["m_PrototypeLink", "m_Flags", "name"]:
            continue
            
        # Optional: remove Pool if not used
        if k == "Pool" and not use_pool:
            continue
            
        new_dict[k] = simplify_value(v)
        
    return new_dict

def main():
    input_path = "Unique_Enchantment_Components.json"
    output_path = "Clean_Enchantments.json"
    
    if not os.path.exists(input_path):
        print(f"Error: {input_path} not found.")
        return

    print(f"Loading {input_path}...")
    with open(input_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    print("Cleaning data...")
    cleaned_data = {}
    for comp_type, instances in data.items():
        cleaned_instances = []
        for inst in instances:
            cleaned_instances.append(clean_dict(inst))
        cleaned_data[comp_type] = cleaned_instances

    print(f"Saving to {output_path}...")
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(cleaned_data, f, indent=2, ensure_ascii=False)

    print("Done!")

if __name__ == "__main__":
    main()
