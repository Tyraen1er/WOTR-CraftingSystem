import os
import json
import re
import sys

# Predefined allowed context variables
ALLOWED_CONTEXT_VARS = {"SpellLevel", "SpellMinCasterLevel"}
# Math functions to ignore
MATH_FUNCTIONS = {"MAX", "MIN", "ABS", "FLOOR", "CEIL", "ROUND"}

def extract_variables(formula_str):
    if not isinstance(formula_str, str):
        return []
    if formula_str.startswith("="):
        formula_str = formula_str[1:]
    # Match words starting with letter/underscore
    words = re.findall(r'\b[a-zA-Z_][a-zA-Z0-9_\.]*\b', formula_str)
    # Filter out functions
    vars_found = [w for w in words if w.upper() not in MATH_FUNCTIONS]
    return vars_found

def validate_json_file(filepath):
    print(f"Validating formulas in {filepath}...")
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            data = json.load(f)
    except Exception as e:
        print(f"Error reading JSON file {filepath}: {e}")
        return False

    # The file could be a list of models directly, or an object containing a "Models" list
    models = []
    if isinstance(data, list):
        models = data
    elif isinstance(data, dict):
        if "Models" in data and isinstance(data["Models"], list):
            models = data["Models"]
        else:
            # Just check if it has a DynamicParams key
            models = [data]

    has_errors = False
    for model in models:
        if not isinstance(model, dict):
            continue
        
        enchant_id = model.get("EnchantId", "Unknown")
        base_name_dict = model.get("BaseName", {})
        enchant_name = base_name_dict.get("enGB", enchant_id) if isinstance(base_name_dict, dict) else str(base_name_dict)
        
        params = model.get("DynamicParams", [])
        if not isinstance(params, list):
            continue
            
        param_names = {p.get("Name") for p in params if isinstance(p, dict) and p.get("Name")}
        
        # Build dependency graph
        graph = {}
        param_by_name = {}
        for p in params:
            if not isinstance(p, dict):
                continue
            name = p.get("Name")
            if not name:
                continue
            param_by_name[name] = p
            graph[name] = []
            
            # Inspect Min, Max, DefaultValue
            for field in ["Min", "Max", "DefaultValue"]:
                val = p.get(field)
                if isinstance(val, str) and val.startswith("="):
                    referenced = extract_variables(val)
                    for ref in referenced:
                        if ref in param_names:
                            if ref != name:
                                graph[name].append(ref)
                        elif ref not in ALLOWED_CONTEXT_VARS:
                            print(f"  [ERROR] Model '{enchant_name}' ({enchant_id}) -> Param '{name}' references undefined variable '{ref}' in formula '{val}'")
                            has_errors = True
        
        # Detect cycles
        visited = set()
        stack = set()
        for name in graph:
            if name not in visited:
                # Cycle detection using DFS
                cycle_found = False
                temp_visited = set()
                temp_stack = set()
                
                def dfs(node):
                    temp_visited.add(node)
                    temp_stack.add(node)
                    for neighbor in graph.get(node, []):
                        if neighbor not in temp_visited:
                            if dfs(neighbor):
                                return True
                        elif neighbor in temp_stack:
                            # Build cycle path
                            print(f"  [ERROR] Model '{enchant_name}' ({enchant_id}) has a circular formula dependency cycle: {node} -> {neighbor}")
                            return True
                    temp_stack.remove(node)
                    return False
                
                if dfs(name):
                    has_errors = True
                    
    return not has_errors

def main():
    config_dir = "ModConfig"
    files_to_check = [
        "CustomEnchants.json",
        "CustomEnchants_Blueprints.json",
        "CustomEnchants_Enchantments.json"
    ]
    
    all_ok = True
    for filename in files_to_check:
        filepath = os.path.join(config_dir, filename)
        if os.path.exists(filepath):
            if not validate_json_file(filepath):
                all_ok = False
        else:
            print(f"Warning: File {filepath} not found, skipping.")
            
    if not all_ok:
        print("Formula validation FAILED.")
        sys.exit(1)
    else:
        print("Formula validation PASSED.")
        sys.exit(0)

if __name__ == "__main__":
    main()
