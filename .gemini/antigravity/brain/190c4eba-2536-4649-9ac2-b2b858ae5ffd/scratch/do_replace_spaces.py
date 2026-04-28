import os
path = r"ModConfig\Localization.json"
with open(path, "rb") as f:
    content = f.read()

# Replace « (b"\xc2\xab\x20") with « + NBSP (b"\xc2\xab\xc2\xa0")
# Replace  » (b"\x20\xc2\xbb") with NBSP + » (b"\xc2\xa0\xc2\xbb")

new_content = content.replace(b"\xc2\xab\x20", b"\xc2\xab\xc2\xa0")
new_content = new_content.replace(b"\x20\xc2\xbb", b"\xc2\xa0\xc2\xbb")

with open(path, "wb") as f:
    f.write(new_content)

print(f"Replaced {content.count(b'\xc2\xab\x20')} open and {content.count(b'\x20\xc2\xbb')} close instances.")
