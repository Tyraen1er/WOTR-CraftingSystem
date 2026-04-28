import os
path = r"ModConfig\Localization.json"
with open(path, "rb") as f:
    content = f.read()

has_space_after_open = b"\xc2\xab\x20" in content
has_space_before_close = b"\x20\xc2\xbb" in content

print(f"Space after open: {has_space_after_open}")
print(f"Space before close: {has_space_before_close}")

# Count them
print(f"Count open: {content.count(b'\xc2\xab\x20')}")
print(f"Count close: {content.count(b'\x20\xc2\xbb')}")
