"""Configure generated Android source, or validate apkanalyzer's final APK XML."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ANDROID = "http://schemas.android.com/apk/res/android"
ET.register_namespace("android", ANDROID)
attr = lambda name: f"{{{ANDROID}}}{name}"


def process(path, configure=False):
    tree = ET.parse(path)
    root = tree.getroot()
    application = root.find("application")
    if application is None:
        raise ValueError("Android application element missing")
    permissions = root.findall("uses-permission")
    internet = any(p.get(attr("name")) == "android.permission.INTERNET" for p in permissions)
    if configure:
        if not internet:
            ET.SubElement(root, "uses-permission", {attr("name"): "android.permission.INTERNET"})
        application.set(attr("label"), "OdinVault")
        application.set(attr("usesCleartextTraffic"), "true")
        tree.write(path, encoding="utf-8", xml_declaration=True)
        return process(path)
    if not internet:
        raise ValueError("Release APK lacks INTERNET permission")
    if application.get(attr("usesCleartextTraffic")) != "true":
        raise ValueError("Release APK does not allow HTTP agents")
    if application.get(attr("networkSecurityConfig")):
        raise ValueError("Network security config overrides cleartext flag; review its policy")
    print(f"Validated INTERNET and HTTP access: {path}")


if __name__ == "__main__":
    process(Path(sys.argv[2]), configure=sys.argv[1] == "configure")
