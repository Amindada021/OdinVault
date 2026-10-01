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
    notifications = any(
        p.get(attr("name")) == "android.permission.POST_NOTIFICATIONS"
        for p in permissions
    )
    if configure:
        if not internet:
            ET.SubElement(root, "uses-permission", {attr("name"): "android.permission.INTERNET"})
        if not notifications:
            ET.SubElement(
                root,
                "uses-permission",
                {attr("name"): "android.permission.POST_NOTIFICATIONS"},
            )
        application.set(attr("label"), "OdinVault")
        if not any(x.get(attr("name")) == ".BackupDownloadService" for x in application.findall("service")):
            ET.SubElement(application, "service", {attr("name"): ".BackupDownloadService", attr("exported"): "false", attr("foregroundServiceType"): "dataSync"})
        for permission in ("android.permission.FOREGROUND_SERVICE", "android.permission.FOREGROUND_SERVICE_DATA_SYNC"):
            if not any(p.get(attr("name")) == permission for p in root.findall("uses-permission")):
                ET.SubElement(root, "uses-permission", {attr("name"): permission})
        if not any(x.get(attr("name")) == ".BackupDownloadService" for x in application.findall("service")):
            ET.SubElement(application, "service", {attr("name"): ".BackupDownloadService", attr("exported"): "false", attr("foregroundServiceType"): "dataSync"})
        for permission in ("android.permission.FOREGROUND_SERVICE", "android.permission.FOREGROUND_SERVICE_DATA_SYNC"):
            if not any(p.get(attr("name")) == permission for p in root.findall("uses-permission")):
                ET.SubElement(root, "uses-permission", {attr("name"): permission})
        application.set(attr("usesCleartextTraffic"), "true")
        tree.write(path, encoding="utf-8", xml_declaration=True)
        return process(path)
    if not internet:
        raise ValueError("Release APK lacks INTERNET permission")
    if not notifications:
        raise ValueError("Release APK lacks POST_NOTIFICATIONS permission")
    if not any(x.get(attr("name")) == ".BackupDownloadService" for x in application.findall("service")):
        raise ValueError("Release APK lacks BackupDownloadService")
    if not any(x.get(attr("name")) == ".BackupDownloadService" for x in application.findall("service")):
        raise ValueError("Release APK lacks BackupDownloadService")
    if application.get(attr("usesCleartextTraffic")) != "true":
        raise ValueError("Release APK does not allow HTTP agents")
    if application.get(attr("networkSecurityConfig")):
        raise ValueError("Network security config overrides cleartext flag; review its policy")
    print(f"Validated INTERNET, notifications and HTTP access: {path}")


if __name__ == "__main__":
    process(Path(sys.argv[2]), configure=sys.argv[1] == "configure")
