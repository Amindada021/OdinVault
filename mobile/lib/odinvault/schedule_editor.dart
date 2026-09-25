import 'package:flutter/material.dart';

enum ScheduleMode { manual, onceDaily, twiceDaily, advanced }

class ScheduleValue {
  const ScheduleValue({
    required this.mode,
    required this.first,
    required this.second,
    required this.advancedCron,
  });

  final ScheduleMode mode;
  final TimeOfDay first;
  final TimeOfDay second;
  final String advancedCron;

  factory ScheduleValue.fromCron(String? cron) {
    if (cron == null || cron.trim().isEmpty) {
      return const ScheduleValue(
        mode: ScheduleMode.manual,
        first: TimeOfDay(hour: 2, minute: 0),
        second: TimeOfDay(hour: 14, minute: 0),
        advancedCron: '',
      );
    }

    final parsed = _parseSimpleUtcCron(cron);
    if (parsed != null) {
      final localTimes = parsed.map(_utcToLocal).toList();
      if (localTimes.length == 1) {
        return ScheduleValue(
          mode: ScheduleMode.onceDaily,
          first: localTimes[0],
          second: const TimeOfDay(hour: 14, minute: 0),
          advancedCron: '',
        );
      }
      if (localTimes.length == 2) {
        return ScheduleValue(
          mode: ScheduleMode.twiceDaily,
          first: localTimes[0],
          second: localTimes[1],
          advancedCron: '',
        );
      }
    }

    return ScheduleValue(
      mode: ScheduleMode.advanced,
      first: const TimeOfDay(hour: 2, minute: 0),
      second: const TimeOfDay(hour: 14, minute: 0),
      advancedCron: cron.trim(),
    );
  }

  String? toCron() {
    switch (mode) {
      case ScheduleMode.manual:
        return null;
      case ScheduleMode.onceDaily:
        final utc = _localToUtc(first);
        return '${utc.minute} ${utc.hour} * * *';
      case ScheduleMode.twiceDaily:
        final firstUtc = _localToUtc(first);
        final secondUtc = _localToUtc(second);
        if (firstUtc.minute != secondUtc.minute) {
          throw const FormatException(
            'برای اجرای دو بار در روز، دقیقه دو زمان باید یکسان باشد.',
          );
        }
        final hours = <int>{firstUtc.hour, secondUtc.hour}.toList()..sort();
        if (hours.length != 2) {
          throw const FormatException('دو ساعت متفاوت انتخاب کنید.');
        }
        return '${firstUtc.minute} ${hours.join(',')} * * *';
      case ScheduleMode.advanced:
        final value = advancedCron.trim();
        return value.isEmpty ? null : value;
    }
  }

  String get summary {
    switch (mode) {
      case ScheduleMode.manual:
        return 'دستی';
      case ScheduleMode.onceDaily:
        return 'هر روز ساعت ${formatTime(first)}';
      case ScheduleMode.twiceDaily:
        return 'هر روز ${formatTime(first)} و ${formatTime(second)}';
      case ScheduleMode.advanced:
        return advancedCron.trim().isEmpty ? 'دستی' : 'پیشرفته';
    }
  }

  ScheduleValue copyWith({
    ScheduleMode? mode,
    TimeOfDay? first,
    TimeOfDay? second,
    String? advancedCron,
  }) =>
      ScheduleValue(
        mode: mode ?? this.mode,
        first: first ?? this.first,
        second: second ?? this.second,
        advancedCron: advancedCron ?? this.advancedCron,
      );

  static String displayCron(String? cron) => ScheduleValue.fromCron(cron).summary;

  static String formatTime(TimeOfDay value) =>
      '${value.hour.toString().padLeft(2, '0')}:${value.minute.toString().padLeft(2, '0')}';

  static List<TimeOfDay>? _parseSimpleUtcCron(String cron) {
    final parts = cron.trim().split(RegExp(r'\s+'));
    if (parts.length != 5 || parts[2] != '*' || parts[3] != '*' || parts[4] != '*') {
      return null;
    }

    final minute = int.tryParse(parts[0]);
    if (minute == null || minute < 0 || minute > 59) return null;

    final hours = parts[1]
        .split(',')
        .map(int.tryParse)
        .whereType<int>()
        .where((hour) => hour >= 0 && hour <= 23)
        .toList();

    if (hours.isEmpty || hours.length > 2) return null;
    return hours.map((hour) => TimeOfDay(hour: hour, minute: minute)).toList();
  }

  static TimeOfDay _localToUtc(TimeOfDay local) {
    final minutes = (local.hour * 60 + local.minute - 210) % 1440;
    return TimeOfDay(hour: minutes ~/ 60, minute: minutes % 60);
  }

  static TimeOfDay _utcToLocal(TimeOfDay utc) {
    final minutes = (utc.hour * 60 + utc.minute + 210) % 1440;
    return TimeOfDay(hour: minutes ~/ 60, minute: minutes % 60);
  }

}

class ScheduleEditor extends StatefulWidget {
  const ScheduleEditor({
    super.key,
    required this.value,
    required this.onChanged,
  });

  final ScheduleValue value;
  final ValueChanged<ScheduleValue> onChanged;

  @override
  State<ScheduleEditor> createState() => _ScheduleEditorState();
}

class _ScheduleEditorState extends State<ScheduleEditor> {
  late ScheduleValue value = widget.value;
  late final TextEditingController advanced =
      TextEditingController(text: widget.value.advancedCron);

  @override
  void dispose() {
    advanced.dispose();
    super.dispose();
  }

  void update(ScheduleValue next) {
    setState(() => value = next);
    widget.onChanged(next);
  }

  Future<void> pick(bool second) async {
    final initial = second ? value.second : value.first;
    final picked = await showTimePicker(
      context: context,
      initialTime: initial,
      helpText: second ? 'ساعت دوم بکاپ' : 'ساعت بکاپ',
      cancelText: 'انصراف',
      confirmText: 'تأیید',
    );
    if (picked == null) return;
    update(second ? value.copyWith(second: picked) : value.copyWith(first: picked));
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        DropdownButtonFormField<ScheduleMode>(
          initialValue: value.mode,
          decoration: const InputDecoration(
            labelText: 'زمان‌بندی',
            prefixIcon: Icon(Icons.schedule),
          ),
          items: const [
            DropdownMenuItem(value: ScheduleMode.manual, child: Text('دستی')),
            DropdownMenuItem(value: ScheduleMode.onceDaily, child: Text('روزانه یک‌بار')),
            DropdownMenuItem(value: ScheduleMode.twiceDaily, child: Text('روزانه دو بار')),
            DropdownMenuItem(value: ScheduleMode.advanced, child: Text('پیشرفته (Cron)')),
          ],
          onChanged: (mode) {
            if (mode != null) update(value.copyWith(mode: mode));
          },
        ),
        if (value.mode == ScheduleMode.onceDaily || value.mode == ScheduleMode.twiceDaily) ...[
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: () => pick(false),
            icon: const Icon(Icons.access_time),
            label: Text('ساعت اول: ${ScheduleValue.formatTime(value.first)}'),
          ),
        ],
        if (value.mode == ScheduleMode.twiceDaily) ...[
          const SizedBox(height: 8),
          OutlinedButton.icon(
            onPressed: () => pick(true),
            icon: const Icon(Icons.access_time_filled),
            label: Text('ساعت دوم: ${ScheduleValue.formatTime(value.second)}'),
          ),
        ],
        if (value.mode == ScheduleMode.advanced) ...[
          const SizedBox(height: 12),
          TextField(
            controller: advanced,
            textDirection: TextDirection.ltr,
            decoration: const InputDecoration(
              labelText: 'Cron پیشرفته (UTC)',
              hintText: '0 2,14 * * *',
            ),
            onChanged: (text) => update(value.copyWith(advancedCron: text)),
          ),
        ],
        const SizedBox(height: 8),
        Text(
          value.mode == ScheduleMode.advanced
              ? 'Cron پیشرفته مستقیماً با UTC ذخیره می‌شود.'
              : 'همه ساعت‌ها به وقت تهران هستند؛ مستقل از ساعت گوشی و سرور.',
          style: Theme.of(context).textTheme.bodySmall,
        ),
      ],
    );
  }
}
