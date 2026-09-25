class OdinVaultServer {
  const OdinVaultServer({required this.id, required this.name, required this.baseUrl, required this.apiKey});
  final String id;
  final String name;
  final String baseUrl;
  final String apiKey;
  Map<String, dynamic> toJson() => {'id': id, 'name': name, 'baseUrl': baseUrl, 'apiKey': apiKey};
  factory OdinVaultServer.fromJson(Map<String, dynamic> json) => OdinVaultServer(
        id: json['id'] as String,
        name: json['name'] as String,
        baseUrl: json['baseUrl'] as String,
        apiKey: json['apiKey'] as String? ?? '',
      );
}

class OdinVaultDatabase {
  const OdinVaultDatabase({
    required this.id,
    required this.name,
    required this.databaseName,
    required this.host,
    required this.port,
    required this.username,
    required this.hasPassword,
    required this.trustServerCertificate,
    required this.isEnabled,
    required this.backupDirectory,
    required this.scheduleCron,
    required this.maxLocalBackups,
    required this.verifyAfterBackup,
    required this.policyEnabled,
  });
  final String id;
  final String name;
  final String databaseName;
  final String host;
  final int? port;
  final String username;
  final bool hasPassword;
  final bool trustServerCertificate;
  final bool isEnabled;
  final String backupDirectory;
  final String? scheduleCron;
  final int maxLocalBackups;
  final bool verifyAfterBackup;
  final bool policyEnabled;

  factory OdinVaultDatabase.fromJson(Map<String, dynamic> json) {
    final policy = json['policy'] is Map ? Map<String, dynamic>.from(json['policy'] as Map) : <String, dynamic>{};
    return OdinVaultDatabase(
      id: json['id'].toString(),
      name: json['name']?.toString() ?? '',
      databaseName: json['databaseName']?.toString() ?? '',
      host: json['host']?.toString() ?? '',
      port: (json['port'] as num?)?.toInt(),
      username: json['username']?.toString() ?? '',
      hasPassword: json['hasPassword'] == true,
      trustServerCertificate: json['trustServerCertificate'] != false,
      isEnabled: json['isEnabled'] == true,
      backupDirectory: policy['backupDirectory']?.toString() ?? '',
      scheduleCron: policy['scheduleCron']?.toString(),
      maxLocalBackups: (policy['maxLocalBackups'] as num?)?.toInt() ?? 7,
      verifyAfterBackup: policy['verifyAfterBackup'] != false,
      policyEnabled: policy['isEnabled'] != false,
    );
  }
}

class OdinVaultBackup {
  const OdinVaultBackup({required this.id, required this.fileName, required this.status, required this.verificationStatus, required this.sizeBytes, required this.startedAtUtc, required this.completedAtUtc, required this.localFileAvailable, required this.error});
  final String id;
  final String fileName;
  final int status;
  final int verificationStatus;
  final int? sizeBytes;
  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;
  final bool localFileAvailable;
  final String? error;
  factory OdinVaultBackup.fromJson(Map<String, dynamic> json) => OdinVaultBackup(
        id: json['id'].toString(), fileName: json['fileName']?.toString() ?? '', status: (json['status'] as num?)?.toInt() ?? 0,
        verificationStatus: (json['verificationStatus'] as num?)?.toInt() ?? 0, sizeBytes: (json['sizeBytes'] as num?)?.toInt(),
        startedAtUtc: DateTime.tryParse(json['startedAtUtc']?.toString() ?? ''), completedAtUtc: DateTime.tryParse(json['completedAtUtc']?.toString() ?? ''),
        localFileAvailable: json['localFileAvailable'] == true, error: json['error']?.toString(),
      );
}

class OdinVaultStorageTarget {
  const OdinVaultStorageTarget({
    required this.id,
    required this.name,
    required this.type,
    required this.isEnabled,
    required this.isConnected,
    this.baseUrl,
    this.accountEmail,
    this.folderId,
  });
  final String id;
  final String name;
  final int type;
  final bool isEnabled;
  final bool isConnected;
  final String? baseUrl;
  final String? accountEmail;
  final String? folderId;
  factory OdinVaultStorageTarget.fromJson(Map<String, dynamic> json) => OdinVaultStorageTarget(
        id: json['id'].toString(),
        name: json['name']?.toString() ?? '',
        type: (json['type'] as num?)?.toInt() ?? 0,
        isEnabled: json['isEnabled'] == true,
        isConnected: json['isConnected'] == true,
        baseUrl: json['baseUrl']?.toString(),
        accountEmail: json['accountEmail']?.toString(),
        folderId: json['folderId']?.toString(),
      );
}

class OdinVaultReplica {
  const OdinVaultReplica({required this.id, required this.storageTargetId, required this.status, this.remotePath, this.error});
  final String id;
  final String storageTargetId;
  final int status;
  final String? remotePath;
  final String? error;
  factory OdinVaultReplica.fromJson(Map<String, dynamic> json) => OdinVaultReplica(
        id: json['id'].toString(), storageTargetId: json['storageTargetId'].toString(), status: (json['status'] as num?)?.toInt() ?? 0,
        remotePath: json['remotePath']?.toString(), error: json['error']?.toString(),
      );
}

class GoogleDrivePairingStart {
  const GoogleDrivePairingStart({required this.authorizationUrl, required this.state});
  final String authorizationUrl;
  final String state;
}

class GoogleDrivePairingStatus {
  const GoogleDrivePairingStatus({required this.status, this.storageTargetId, this.error});
  final String status;
  final String? storageTargetId;
  final String? error;
}


class DiscoveredDatabase {
  const DiscoveredDatabase({
    required this.name,
    required this.databaseId,
    required this.state,
    required this.recoveryModel,
    required this.isSystem,
    required this.hasAccess,
    required this.isRegistered,
    required this.canBackup,
  });

  final String name;
  final int databaseId;
  final String state;
  final String recoveryModel;
  final bool isSystem;
  final bool hasAccess;
  final bool isRegistered;
  final bool canBackup;

  factory DiscoveredDatabase.fromJson(Map<String, dynamic> json) => DiscoveredDatabase(
        name: json['name']?.toString() ?? '',
        databaseId: (json['databaseId'] as num?)?.toInt() ?? 0,
        state: json['state']?.toString() ?? '',
        recoveryModel: json['recoveryModel']?.toString() ?? '',
        isSystem: json['isSystem'] == true,
        hasAccess: json['hasAccess'] == true,
        isRegistered: json['isRegistered'] == true,
        canBackup: json['canBackup'] == true,
      );
}

class DatabaseStorageLink {
  const DatabaseStorageLink({
    required this.id,
    required this.name,
    required this.type,
    required this.isEnabled,
    required this.linkEnabled,
  });

  final String id;
  final String name;
  final int type;
  final bool isEnabled;
  final bool linkEnabled;

  factory DatabaseStorageLink.fromJson(Map<String, dynamic> json) => DatabaseStorageLink(
        id: json['id'].toString(),
        name: json['name']?.toString() ?? '',
        type: (json['type'] as num?)?.toInt() ?? 0,
        isEnabled: json['isEnabled'] == true,
        linkEnabled: json['linkEnabled'] == true,
      );
}
