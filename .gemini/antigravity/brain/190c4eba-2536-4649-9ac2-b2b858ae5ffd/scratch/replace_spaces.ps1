$path = "ModConfig\Localization.json"
$content = [System.IO.File]::ReadAllText($path)
$nbsp = [char]160
$content = $content.Replace("« ", "«" + $nbsp).Replace(" »", $nbsp + "»")
[System.IO.File]::WriteAllText($path, $content)
