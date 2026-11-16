// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

import '../theme.dart';

/// The single card shape used by every top-level section: one consistent
/// title row, one consistent content inset, one consistent external margin.
/// Sections no longer need to hand-tune padding or rely on parent `SizedBox`s.
final class SectionCard extends StatelessWidget {
  const SectionCard({
    super.key,
    required this.title,
    required this.child,
    this.icon,
    this.actions = const <Widget>[],
    this.expandChild = false,
    this.trailing,
  });

  final String title;
  final IconData? icon;
  final List<Widget> actions;
  final Widget child;
  final bool expandChild;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final content = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: expandChild ? MainAxisSize.max : MainAxisSize.min,
      children: [
        Row(
          children: [
            if (icon != null) ...[
              Icon(icon, size: 20, color: theme.colorScheme.primary),
              const SizedBox(width: AppSpacing.s),
            ],
            Expanded(
              child: Text(
                title,
                style: theme.textTheme.titleMedium
                    ?.copyWith(fontWeight: FontWeight.w600),
              ),
            ),
            ...actions,
          ],
        ),
        if (trailing != null) ...[
          const SizedBox(height: AppSpacing.xs),
          DefaultTextStyle(
            style: theme.textTheme.bodySmall!
                .copyWith(color: theme.colorScheme.onSurfaceVariant),
            child: trailing!,
          ),
        ],
        const SizedBox(height: AppSpacing.m),
        expandChild ? Expanded(child: child) : child,
      ],
    );
    return Card(
      margin: EdgeInsets.zero,
      child: Padding(padding: AppSpacing.cardInsets, child: content),
    );
  }
}

/// Binds a label to its control so a narrow layout can never split them into
/// different rows, and every field shares the same vertical rhythm.
final class LabeledField extends StatelessWidget {
  const LabeledField({super.key, required this.label, required this.child});

  final String label;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          label,
          style: theme.textTheme.labelMedium
              ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
        ),
        const SizedBox(height: AppSpacing.s),
        child,
      ],
    );
  }
}
