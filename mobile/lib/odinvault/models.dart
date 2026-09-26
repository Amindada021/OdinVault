DateTime? parseAgentUtc(Object? value) {
  final text = value?.toString();
  if (text == null || text.isEmpty) return null;
  final parsed = DateTime.tryParse(text);
  if (parsed == null) return null;
  if (parsed.isUtc || RegExp(r'[+-]\\d\\d:\\d\\d$').hasMatch(text)) {
    return parsed.toUtc();
  }
  return DateTime.utc(
    parsed.year,
    parsed.month,
    parsed.day,
    parsed.hour,
    parsed.minute,
    parsed.second,
    parsed.millisecond,
    parsed.microsecond,
  );
}

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
        startedAtUtc: parseAgentUtc(json['startedAtUtc']), completedAtUtc: parseAgentUtc(json['completedAtUtc']),
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

class OdinVaultDashboard {
  const OdinVaultDashboard({
    required this.utc,
    required this.status,
    required this.protectionStatus,
    required this.enabledDatabases,
    required this.protectedDatabases,
    required this.activeJobs,
    required this.failedJobsLast24Hours,
    required this.storageFreeBytes,
    required this.attention,
    required this.recentActivity,
  });

  final DateTime? utc;
  final String status;
  final String protectionStatus;
  final int enabledDatabases;
  final int protectedDatabases;
  final int activeJobs;
  final int failedJobsLast24Hours;
  final int? storageFreeBytes;
  final List<OdinVaultDashboardAttention> attention;
  final List<OdinVaultDashboardActivity> recentActivity;

  factory OdinVaultDashboard.fromJson(Map<String, dynamic> json) => OdinVaultDashboard(
        utc: parseAgentUtc(json['utc']),
        status: json['status']?.toString() ?? '',
        protectionStatus: json['protectionStatus']?.toString() ?? '',
        enabledDatabases: (json['enabledDatabases'] as num?)?.toInt() ?? 0,
        protectedDatabases: (json['protectedDatabases'] as num?)?.toInt() ?? 0,
        activeJobs: (json['activeJobs'] as num?)?.toInt() ?? 0,
        failedJobsLast24Hours: (json['failedJobsLast24Hours'] as num?)?.toInt() ?? 0,
        storageFreeBytes: (json['storageFreeBytes'] as num?)?.toInt(),
        attention: (json['attention'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultDashboardAttention.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
        recentActivity: (json['recentActivity'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultDashboardActivity.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultDashboardAttention {
  const OdinVaultDashboardAttention({
    required this.severity,
    required this.databaseId,
    required this.databaseName,
    required this.title,
    required this.message,
    required this.occurredAtUtc,
  });

  final String severity;
  final String databaseId;
  final String databaseName;
  final String title;
  final String message;
  final DateTime? occurredAtUtc;

  factory OdinVaultDashboardAttention.fromJson(Map<String, dynamic> json) =>
      OdinVaultDashboardAttention(
        severity: json['severity']?.toString() ?? '',
        databaseId: json['databaseId']?.toString() ?? '',
        databaseName: json['databaseName']?.toString() ?? '',
        title: json['title']?.toString() ?? '',
        message: json['message']?.toString() ?? '',
        occurredAtUtc: parseAgentUtc(json['occurredAtUtc']),
      );
}

class OdinVaultDashboardActivity {
  const OdinVaultDashboardActivity({
    required this.databaseId,
    required this.databaseName,
    required this.backupId,
    required this.status,
    required this.verificationStatus,
    required this.sizeBytes,
    required this.startedAtUtc,
    required this.completedAtUtc,
    required this.error,
  });

  final String databaseId;
  final String databaseName;
  final String backupId;
  final int status;
  final int verificationStatus;
  final int? sizeBytes;
  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;
  final String? error;

  factory OdinVaultDashboardActivity.fromJson(Map<String, dynamic> json) =>
      OdinVaultDashboardActivity(
        databaseId: json['databaseId']?.toString() ?? '',
        databaseName: json['databaseName']?.toString() ?? '',
        backupId: json['backupId']?.toString() ?? '',
        status: (json['status'] as num?)?.toInt() ?? 0,
        verificationStatus: (json['verificationStatus'] as num?)?.toInt() ?? 0,
        sizeBytes: (json['sizeBytes'] as num?)?.toInt(),
        startedAtUtc: parseAgentUtc(json['startedAtUtc']),
        completedAtUtc: parseAgentUtc(json['completedAtUtc']),
        error: json['error']?.toString(),
      );
}

class OdinVaultAlerts {
  const OdinVaultAlerts({
    required this.utc,
    required this.unreadCount,
    required this.alerts,
  });

  final DateTime? utc;
  final int unreadCount;
  final List<OdinVaultAlert> alerts;

  factory OdinVaultAlerts.fromJson(Map<String, dynamic> json) => OdinVaultAlerts(
        utc: parseAgentUtc(json['utc']),
        unreadCount: (json['unreadCount'] as num?)?.toInt() ?? 0,
        alerts: (json['alerts'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultAlert.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultAlert {
  const OdinVaultAlert({
    required this.key,
    required this.severity,
    required this.category,
    required this.databaseId,
    required this.databaseName,
    required this.title,
    required this.message,
    required this.occurredAtUtc,
    required this.isRead,
  });

  final String key;
  final String severity;
  final String category;
  final String? databaseId;
  final String databaseName;
  final String title;
  final String message;
  final DateTime? occurredAtUtc;
  final bool isRead;

  factory OdinVaultAlert.fromJson(Map<String, dynamic> json) => OdinVaultAlert(
        key: json['key']?.toString() ?? '',
        severity: json['severity']?.toString() ?? '',
        category: json['category']?.toString() ?? '',
        databaseId: json['databaseId']?.toString(),
        databaseName: json['databaseName']?.toString() ?? '',
        title: json['title']?.toString() ?? '',
        message: json['message']?.toString() ?? '',
        occurredAtUtc: parseAgentUtc(json['occurredAtUtc']),
        isRead: json['isRead'] == true,
      );
}



class OdinVaultBackupOverview {
  const OdinVaultBackupOverview({
    required this.utc,
    required this.jobs,
    required this.backups,
  });

  final DateTime? utc;
  final List<OdinVaultBackupJob> jobs;
  final List<OdinVaultBackupHistoryItem> backups;

  factory OdinVaultBackupOverview.fromJson(Map<String, dynamic> json) =>
      OdinVaultBackupOverview(
        utc: parseAgentUtc(json['utc']),
        jobs: (json['jobs'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultBackupJob.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
        backups: (json['backups'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultBackupHistoryItem.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultBackupJob {
  const OdinVaultBackupJob({
    required this.id,
    required this.databaseEndpointId,
    required this.databaseName,
    required this.status,
    required this.stage,
    required this.percent,
    required this.createdAtUtc,
    required this.startedAtUtc,
    required this.updatedAtUtc,
    required this.completedAtUtc,
    required this.backupRecordId,
    required this.errorCode,
    required this.errorMessage,
  });

  final String id;
  final String databaseEndpointId;
  final String databaseName;
  final int status;
  final String stage;
  final int? percent;
  final DateTime? createdAtUtc;
  final DateTime? startedAtUtc;
  final DateTime? updatedAtUtc;
  final DateTime? completedAtUtc;
  final String? backupRecordId;
  final String? errorCode;
  final String? errorMessage;

  factory OdinVaultBackupJob.fromJson(Map<String, dynamic> json) => OdinVaultBackupJob(
        id: json['id']?.toString() ?? '',
        databaseEndpointId: json['databaseEndpointId']?.toString() ?? '',
        databaseName: json['databaseName']?.toString() ?? '',
        status: (json['status'] as num?)?.toInt() ?? 0,
        stage: json['stage']?.toString() ?? '',
        percent: (json['percent'] as num?)?.toInt(),
        createdAtUtc: parseAgentUtc(json['createdAtUtc']),
        startedAtUtc: parseAgentUtc(json['startedAtUtc']),
        updatedAtUtc: parseAgentUtc(json['updatedAtUtc']),
        completedAtUtc: parseAgentUtc(json['completedAtUtc']),
        backupRecordId: json['backupRecordId']?.toString(),
        errorCode: json['errorCode']?.toString(),
        errorMessage: json['errorMessage']?.toString(),
      );
}

class OdinVaultBackupHistoryItem {
  const OdinVaultBackupHistoryItem({
    required this.id,
    required this.databaseEndpointId,
    required this.databaseName,
    required this.status,
    required this.verificationStatus,
    required this.sizeBytes,
    required this.startedAtUtc,
    required this.completedAtUtc,
    required this.localFileAvailable,
    required this.error,
  });

  final String id;
  final String databaseEndpointId;
  final String databaseName;
  final int status;
  final int verificationStatus;
  final int? sizeBytes;
  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;
  final bool localFileAvailable;
  final String? error;

  factory OdinVaultBackupHistoryItem.fromJson(Map<String, dynamic> json) =>
      OdinVaultBackupHistoryItem(
        id: json['id']?.toString() ?? '',
        databaseEndpointId: json['databaseEndpointId']?.toString() ?? '',
        databaseName: json['databaseName']?.toString() ?? '',
        status: (json['status'] as num?)?.toInt() ?? 0,
        verificationStatus: (json['verificationStatus'] as num?)?.toInt() ?? 0,
        sizeBytes: (json['sizeBytes'] as num?)?.toInt(),
        startedAtUtc: parseAgentUtc(json['startedAtUtc']),
        completedAtUtc: parseAgentUtc(json['completedAtUtc']),
        localFileAvailable: json['localFileAvailable'] == true,
        error: json['error']?.toString(),
      );
}

class OdinVaultRestorePreflight {
  const OdinVaultRestorePreflight({
    required this.backupId,
    required this.databaseEndpointId,
    required this.endpointName,
    required this.sourceDatabaseName,
    required this.targetDatabaseName,
    required this.backupFileName,
    required this.backupSizeBytes,
    required this.verificationStatus,
    required this.productVersion,
    required this.dataDirectory,
    required this.logDirectory,
    required this.files,
  });

  final String backupId;
  final String databaseEndpointId;
  final String endpointName;
  final String sourceDatabaseName;
  final String targetDatabaseName;
  final String backupFileName;
  final int? backupSizeBytes;
  final int verificationStatus;
  final String productVersion;
  final String dataDirectory;
  final String logDirectory;
  final List<OdinVaultRestoreFilePlan> files;

  factory OdinVaultRestorePreflight.fromJson(Map<String, dynamic> json) =>
      OdinVaultRestorePreflight(
        backupId: json['backupId']?.toString() ?? '',
        databaseEndpointId: json['databaseEndpointId']?.toString() ?? '',
        endpointName: json['endpointName']?.toString() ?? '',
        sourceDatabaseName: json['sourceDatabaseName']?.toString() ?? '',
        targetDatabaseName: json['targetDatabaseName']?.toString() ?? '',
        backupFileName: json['backupFileName']?.toString() ?? '',
        backupSizeBytes: (json['backupSizeBytes'] as num?)?.toInt(),
        verificationStatus: (json['verificationStatus'] as num?)?.toInt() ?? 0,
        productVersion: json['productVersion']?.toString() ?? '',
        dataDirectory: json['dataDirectory']?.toString() ?? '',
        logDirectory: json['logDirectory']?.toString() ?? '',
        files: (json['files'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultRestoreFilePlan.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultRestoreFilePlan {
  const OdinVaultRestoreFilePlan({
    required this.logicalName,
    required this.type,
    required this.targetPath,
  });

  final String logicalName;
  final String type;
  final String targetPath;

  factory OdinVaultRestoreFilePlan.fromJson(Map<String, dynamic> json) =>
      OdinVaultRestoreFilePlan(
        logicalName: json['logicalName']?.toString() ?? '',
        type: json['type']?.toString() ?? '',
        targetPath: json['targetPath']?.toString() ?? '',
      );
}

class OdinVaultRestoreResult {
  const OdinVaultRestoreResult({
    required this.backupId,
    required this.databaseEndpointId,
    required this.targetDatabaseName,
    required this.completedAtUtc,
    required this.durationSeconds,
  });

  final String backupId;
  final String databaseEndpointId;
  final String targetDatabaseName;
  final DateTime? completedAtUtc;
  final double durationSeconds;

  factory OdinVaultRestoreResult.fromJson(Map<String, dynamic> json) =>
      OdinVaultRestoreResult(
        backupId: json['backupId']?.toString() ?? '',
        databaseEndpointId: json['databaseEndpointId']?.toString() ?? '',
        targetDatabaseName: json['targetDatabaseName']?.toString() ?? '',
        completedAtUtc: parseAgentUtc(json['completedAtUtc']),
        durationSeconds: (json['durationSeconds'] as num?)?.toDouble() ?? 0,
      );
}


class OdinVaultBackupReport {
  const OdinVaultBackupReport({
    required this.rangeDays,
    required this.fromUtc,
    required this.toUtc,
    required this.summary,
    required this.daily,
    required this.databases,
  });

  final int rangeDays;
  final DateTime? fromUtc;
  final DateTime? toUtc;
  final OdinVaultBackupReportSummary summary;
  final List<OdinVaultBackupReportDaily> daily;
  final List<OdinVaultBackupReportDatabase> databases;

  factory OdinVaultBackupReport.fromJson(Map<String, dynamic> json) =>
      OdinVaultBackupReport(
        rangeDays: (json['rangeDays'] as num?)?.toInt() ?? 30,
        fromUtc: parseAgentUtc(json['fromUtc']),
        toUtc: parseAgentUtc(json['toUtc']),
        summary: OdinVaultBackupReportSummary.fromJson(
          json['summary'] is Map
              ? Map<String, dynamic>.from(json['summary'] as Map)
              : <String, dynamic>{},
        ),
        daily: (json['daily'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultBackupReportDaily.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
        databases: (json['databases'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultBackupReportDatabase.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultBackupReportSummary {
  const OdinVaultBackupReportSummary({
    required this.totalBackups,
    required this.succeeded,
    required this.failed,
    required this.verifyFailed,
    required this.replicaFailed,
    required this.successRate,
    required this.averageDurationSeconds,
    required this.totalSuccessfulBytes,
    required this.enabledDatabases,
    required this.protectedDatabases,
    required this.storageFreeBytes,
  });

  final int totalBackups;
  final int succeeded;
  final int failed;
  final int verifyFailed;
  final int replicaFailed;
  final double? successRate;
  final double? averageDurationSeconds;
  final int totalSuccessfulBytes;
  final int enabledDatabases;
  final int protectedDatabases;
  final int? storageFreeBytes;

  factory OdinVaultBackupReportSummary.fromJson(Map<String, dynamic> json) =>
      OdinVaultBackupReportSummary(
        totalBackups: (json['totalBackups'] as num?)?.toInt() ?? 0,
        succeeded: (json['succeeded'] as num?)?.toInt() ?? 0,
        failed: (json['failed'] as num?)?.toInt() ?? 0,
        verifyFailed: (json['verifyFailed'] as num?)?.toInt() ?? 0,
        replicaFailed: (json['replicaFailed'] as num?)?.toInt() ?? 0,
        successRate: (json['successRate'] as num?)?.toDouble(),
        averageDurationSeconds: (json['averageDurationSeconds'] as num?)?.toDouble(),
        totalSuccessfulBytes: (json['totalSuccessfulBytes'] as num?)?.toInt() ?? 0,
        enabledDatabases: (json['enabledDatabases'] as num?)?.toInt() ?? 0,
        protectedDatabases: (json['protectedDatabases'] as num?)?.toInt() ?? 0,
        storageFreeBytes: (json['storageFreeBytes'] as num?)?.toInt(),
      );
}

class OdinVaultBackupReportDaily {
  const OdinVaultBackupReportDaily({
    required this.dateUtc,
    required this.succeeded,
    required this.failed,
    required this.verifyFailed,
    required this.totalSizeBytes,
    required this.averageDurationSeconds,
  });

  final DateTime? dateUtc;
  final int succeeded;
  final int failed;
  final int verifyFailed;
  final int totalSizeBytes;
  final double? averageDurationSeconds;

  factory OdinVaultBackupReportDaily.fromJson(Map<String, dynamic> json) =>
      OdinVaultBackupReportDaily(
        dateUtc: parseAgentUtc(json['dateUtc']),
        succeeded: (json['succeeded'] as num?)?.toInt() ?? 0,
        failed: (json['failed'] as num?)?.toInt() ?? 0,
        verifyFailed: (json['verifyFailed'] as num?)?.toInt() ?? 0,
        totalSizeBytes: (json['totalSizeBytes'] as num?)?.toInt() ?? 0,
        averageDurationSeconds: (json['averageDurationSeconds'] as num?)?.toDouble(),
      );
}

class OdinVaultBackupReportDatabase {
  const OdinVaultBackupReportDatabase({
    required this.databaseId,
    required this.databaseName,
    required this.totalBackups,
    required this.succeeded,
    required this.failed,
    required this.verifyFailed,
    required this.replicaFailed,
    required this.successRate,
    required this.averageDurationSeconds,
    required this.latestBackupAtUtc,
    required this.latestBackupStatus,
    required this.latestSizeBytes,
    required this.previousSizeBytes,
    required this.sizeGrowthPercent,
    required this.severity,
  });

  final String databaseId;
  final String databaseName;
  final int totalBackups;
  final int succeeded;
  final int failed;
  final int verifyFailed;
  final int replicaFailed;
  final double? successRate;
  final double? averageDurationSeconds;
  final DateTime? latestBackupAtUtc;
  final int? latestBackupStatus;
  final int? latestSizeBytes;
  final int? previousSizeBytes;
  final double? sizeGrowthPercent;
  final String severity;

  factory OdinVaultBackupReportDatabase.fromJson(Map<String, dynamic> json) =>
      OdinVaultBackupReportDatabase(
        databaseId: json['databaseId']?.toString() ?? '',
        databaseName: json['databaseName']?.toString() ?? '',
        totalBackups: (json['totalBackups'] as num?)?.toInt() ?? 0,
        succeeded: (json['succeeded'] as num?)?.toInt() ?? 0,
        failed: (json['failed'] as num?)?.toInt() ?? 0,
        verifyFailed: (json['verifyFailed'] as num?)?.toInt() ?? 0,
        replicaFailed: (json['replicaFailed'] as num?)?.toInt() ?? 0,
        successRate: (json['successRate'] as num?)?.toDouble(),
        averageDurationSeconds: (json['averageDurationSeconds'] as num?)?.toDouble(),
        latestBackupAtUtc: parseAgentUtc(json['latestBackupAtUtc']),
        latestBackupStatus: (json['latestBackupStatus'] as num?)?.toInt(),
        latestSizeBytes: (json['latestSizeBytes'] as num?)?.toInt(),
        previousSizeBytes: (json['previousSizeBytes'] as num?)?.toInt(),
        sizeGrowthPercent: (json['sizeGrowthPercent'] as num?)?.toDouble(),
        severity: json['severity']?.toString() ?? '',
      );
}

class OdinVaultStorageOverview {
  const OdinVaultStorageOverview({
    required this.local,
    required this.targets,
  });

  final OdinVaultLocalStorageOverview local;
  final List<OdinVaultStorageTargetOverview> targets;

  factory OdinVaultStorageOverview.fromJson(Map<String, dynamic> json) =>
      OdinVaultStorageOverview(
        local: OdinVaultLocalStorageOverview.fromJson(
          json['local'] is Map
              ? Map<String, dynamic>.from(json['local'] as Map)
              : <String, dynamic>{},
        ),
        targets: (json['targets'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultStorageTargetOverview.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultLocalStorageOverview {
  const OdinVaultLocalStorageOverview({
    required this.name,
    required this.directory,
    required this.freeBytes,
    required this.exists,
    required this.writable,
  });

  final String name;
  final String directory;
  final int? freeBytes;
  final bool exists;
  final bool writable;

  factory OdinVaultLocalStorageOverview.fromJson(Map<String, dynamic> json) =>
      OdinVaultLocalStorageOverview(
        name: json['name']?.toString() ?? '',
        directory: json['directory']?.toString() ?? '',
        freeBytes: (json['freeBytes'] as num?)?.toInt(),
        exists: json['exists'] == true,
        writable: json['writable'] == true,
      );
}

class OdinVaultStorageTargetOverview {
  const OdinVaultStorageTargetOverview({
    required this.id,
    required this.name,
    required this.type,
    required this.isEnabled,
    required this.folderId,
    required this.accountEmail,
    required this.baseUrl,
    required this.isConnected,
    required this.linkedDatabases,
    required this.succeededReplicas,
    required this.failedReplicas,
    required this.lastActivityAtUtc,
    required this.lastSuccessAtUtc,
    required this.lastFailureAtUtc,
    required this.lastError,
  });

  final String id;
  final String name;
  final int type;
  final bool isEnabled;
  final String? folderId;
  final String? accountEmail;
  final String? baseUrl;
  final bool isConnected;
  final int linkedDatabases;
  final int succeededReplicas;
  final int failedReplicas;
  final DateTime? lastActivityAtUtc;
  final DateTime? lastSuccessAtUtc;
  final DateTime? lastFailureAtUtc;
  final String? lastError;

  factory OdinVaultStorageTargetOverview.fromJson(Map<String, dynamic> json) =>
      OdinVaultStorageTargetOverview(
        id: json['id']?.toString() ?? '',
        name: json['name']?.toString() ?? '',
        type: (json['type'] as num?)?.toInt() ?? 0,
        isEnabled: json['isEnabled'] == true,
        folderId: json['folderId']?.toString(),
        accountEmail: json['accountEmail']?.toString(),
        baseUrl: json['baseUrl']?.toString(),
        isConnected: json['isConnected'] == true,
        linkedDatabases: (json['linkedDatabases'] as num?)?.toInt() ?? 0,
        succeededReplicas: (json['succeededReplicas'] as num?)?.toInt() ?? 0,
        failedReplicas: (json['failedReplicas'] as num?)?.toInt() ?? 0,
        lastActivityAtUtc: parseAgentUtc(json['lastActivityAtUtc']),
        lastSuccessAtUtc: parseAgentUtc(json['lastSuccessAtUtc']),
        lastFailureAtUtc: parseAgentUtc(json['lastFailureAtUtc']),
        lastError: json['lastError']?.toString(),
      );
}

class OdinVaultDatabaseDetails {
  const OdinVaultDatabaseDetails({
    required this.database,
    required this.protection,
    required this.backups,
    required this.replicas,
  });

  final OdinVaultDatabaseDetailsInfo database;
  final OdinVaultDatabaseProtection protection;
  final List<OdinVaultDatabaseBackupHistory> backups;
  final List<OdinVaultDatabaseReplicaHistory> replicas;

  factory OdinVaultDatabaseDetails.fromJson(Map<String, dynamic> json) =>
      OdinVaultDatabaseDetails(
        database: OdinVaultDatabaseDetailsInfo.fromJson(
          json['database'] is Map
              ? Map<String, dynamic>.from(json['database'] as Map)
              : <String, dynamic>{},
        ),
        protection: OdinVaultDatabaseProtection.fromJson(
          json['protection'] is Map
              ? Map<String, dynamic>.from(json['protection'] as Map)
              : <String, dynamic>{},
        ),
        backups: (json['backups'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultDatabaseBackupHistory.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
        replicas: (json['replicas'] as List? ?? const [])
            .whereType<Map>()
            .map((e) => OdinVaultDatabaseReplicaHistory.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}

class OdinVaultDatabaseDetailsInfo {
  const OdinVaultDatabaseDetailsInfo({
    required this.id,
    required this.name,
    required this.host,
    required this.port,
    required this.databaseName,
    required this.isEnabled,
  });

  final String id;
  final String name;
  final String host;
  final int? port;
  final String databaseName;
  final bool isEnabled;

  factory OdinVaultDatabaseDetailsInfo.fromJson(Map<String, dynamic> json) =>
      OdinVaultDatabaseDetailsInfo(
        id: json['id']?.toString() ?? '',
        name: json['name']?.toString() ?? '',
        host: json['host']?.toString() ?? '',
        port: (json['port'] as num?)?.toInt(),
        databaseName: json['databaseName']?.toString() ?? '',
        isEnabled: json['isEnabled'] == true,
      );
}

class OdinVaultDatabaseProtection {
  const OdinVaultDatabaseProtection({
    required this.isProtected,
    required this.latestBackupAtUtc,
    required this.latestBackupStatus,
    required this.latestVerificationStatus,
    required this.latestBackupSizeBytes,
    required this.latestReplicaSucceeded,
    required this.latestReplicaTotal,
  });

  final bool isProtected;
  final DateTime? latestBackupAtUtc;
  final int? latestBackupStatus;
  final int? latestVerificationStatus;
  final int? latestBackupSizeBytes;
  final int latestReplicaSucceeded;
  final int latestReplicaTotal;

  factory OdinVaultDatabaseProtection.fromJson(Map<String, dynamic> json) =>
      OdinVaultDatabaseProtection(
        isProtected: json['isProtected'] == true,
        latestBackupAtUtc: parseAgentUtc(json['latestBackupAtUtc']),
        latestBackupStatus: (json['latestBackupStatus'] as num?)?.toInt(),
        latestVerificationStatus: (json['latestVerificationStatus'] as num?)?.toInt(),
        latestBackupSizeBytes: (json['latestBackupSizeBytes'] as num?)?.toInt(),
        latestReplicaSucceeded: (json['latestReplicaSucceeded'] as num?)?.toInt() ?? 0,
        latestReplicaTotal: (json['latestReplicaTotal'] as num?)?.toInt() ?? 0,
      );
}

class OdinVaultDatabaseBackupHistory {
  const OdinVaultDatabaseBackupHistory({
    required this.id,
    required this.status,
    required this.verificationStatus,
    required this.sizeBytes,
    required this.startedAtUtc,
    required this.completedAtUtc,
    required this.localFileAvailable,
    required this.error,
  });

  final String id;
  final int status;
  final int verificationStatus;
  final int? sizeBytes;
  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;
  final bool localFileAvailable;
  final String? error;

  factory OdinVaultDatabaseBackupHistory.fromJson(Map<String, dynamic> json) =>
      OdinVaultDatabaseBackupHistory(
        id: json['id']?.toString() ?? '',
        status: (json['status'] as num?)?.toInt() ?? 0,
        verificationStatus: (json['verificationStatus'] as num?)?.toInt() ?? 0,
        sizeBytes: (json['sizeBytes'] as num?)?.toInt(),
        startedAtUtc: parseAgentUtc(json['startedAtUtc']),
        completedAtUtc: parseAgentUtc(json['completedAtUtc']),
        localFileAvailable: json['localFileAvailable'] == true,
        error: json['error']?.toString(),
      );
}

class OdinVaultDatabaseReplicaHistory {
  const OdinVaultDatabaseReplicaHistory({
    required this.backupRecordId,
    required this.storageTargetId,
    required this.name,
    required this.type,
    required this.status,
    required this.sizeBytes,
    required this.contentHashSha256,
    required this.error,
    required this.startedAtUtc,
    required this.completedAtUtc,
  });

  final String backupRecordId;
  final String storageTargetId;
  final String name;
  final int type;
  final int status;
  final int? sizeBytes;
  final String? contentHashSha256;
  final String? error;
  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;

  factory OdinVaultDatabaseReplicaHistory.fromJson(Map<String, dynamic> json) =>
      OdinVaultDatabaseReplicaHistory(
        backupRecordId: json['backupRecordId']?.toString() ?? '',
        storageTargetId: json['storageTargetId']?.toString() ?? '',
        name: json['name']?.toString() ?? '',
        type: (json['type'] as num?)?.toInt() ?? 0,
        status: (json['status'] as num?)?.toInt() ?? 0,
        sizeBytes: (json['sizeBytes'] as num?)?.toInt(),
        contentHashSha256: json['contentHashSha256']?.toString(),
        error: json['error']?.toString(),
        startedAtUtc: parseAgentUtc(json['startedAtUtc']),
        completedAtUtc: parseAgentUtc(json['completedAtUtc']),
      );
}


class OdinVaultDatabaseOverview {
  const OdinVaultDatabaseOverview({
    required this.id,
    required this.name,
    required this.host,
    required this.port,
    required this.databaseName,
    required this.isEnabled,
    required this.isProtected,
    required this.latestBackupAtUtc,
    required this.latestBackupStatus,
    required this.latestVerificationStatus,
    required this.latestBackupSizeBytes,
  });

  final String id;
  final String name;
  final String host;
  final int? port;
  final String databaseName;
  final bool isEnabled;
  final bool isProtected;
  final DateTime? latestBackupAtUtc;
  final int? latestBackupStatus;
  final int? latestVerificationStatus;
  final int? latestBackupSizeBytes;

  factory OdinVaultDatabaseOverview.fromJson(Map<String, dynamic> json) =>
      OdinVaultDatabaseOverview(
        id: json['id']?.toString() ?? '',
        name: json['name']?.toString() ?? '',
        host: json['host']?.toString() ?? '',
        port: (json['port'] as num?)?.toInt(),
        databaseName: json['databaseName']?.toString() ?? '',
        isEnabled: json['isEnabled'] == true,
        isProtected: json['isProtected'] == true,
        latestBackupAtUtc: DateTime.tryParse(
          json['latestBackupAtUtc']?.toString() ?? '',
        ),
        latestBackupStatus: (json['latestBackupStatus'] as num?)?.toInt(),
        latestVerificationStatus:
            (json['latestVerificationStatus'] as num?)?.toInt(),
        latestBackupSizeBytes:
            (json['latestBackupSizeBytes'] as num?)?.toInt(),
      );
}


class OdinVaultStorageConnectionTest {
  const OdinVaultStorageConnectionTest({
    required this.success,
    required this.message,
  });

  final bool success;
  final String message;

  factory OdinVaultStorageConnectionTest.fromJson(Map<String, dynamic> json) =>
      OdinVaultStorageConnectionTest(
        success: json['success'] == true,
        message: json['message']?.toString() ?? '',
      );
}
