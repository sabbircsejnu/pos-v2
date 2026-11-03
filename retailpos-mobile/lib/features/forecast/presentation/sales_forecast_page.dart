import 'package:flutter/material.dart';
import 'package:iranimart_business_mobile/core/domain/mobile_role.dart';
import 'package:iranimart_business_mobile/features/forecast/domain/forecast_presentation_models.dart';
import 'package:iranimart_business_mobile/shared/design_system/content_state_panel.dart';

enum ForecastSection {
  overview('Overview'),
  explanation('Explanation'),
  recommendations('Recommendations');

  const ForecastSection(this.label);

  final String label;
}

final class SalesForecastPage extends StatefulWidget {
  const SalesForecastPage({
    required this.role,
    required this.businessId,
    required this.outletName,
    super.key,
  });

  final MobileRole role;
  final int businessId;
  final String? outletName;

  @override
  State<SalesForecastPage> createState() => _SalesForecastPageState();
}

final class _SalesForecastPageState extends State<SalesForecastPage> {
  ForecastHorizon _horizon = ForecastHorizon.oneMonth;
  ForecastSection _section = ForecastSection.overview;
  ForecastDimension _dimension = ForecastDimension.category;
  final _safetyStockController = TextEditingController();
  final _leadTimeController = TextEditingController();
  final _proposedUnitsController = TextEditingController();

  @override
  void dispose() {
    _safetyStockController.dispose();
    _leadTimeController.dispose();
    _proposedUnitsController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.role != MobileRole.businessOwner) {
      return const ContentStatePanel(
        state: ContentState.unauthorized,
        title: 'Forecast unavailable',
        message: 'This role is not currently granted access to forecasting.',
      );
    }

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      children: [
        Text(
          'Sales Forecasting',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 4),
        Text(
          'Advisory outlook for your ERP-authorized business scope.',
          style: Theme.of(context).textTheme.bodyMedium,
        ),
        const SizedBox(height: 12),
        _ScopeCard(
          businessId: widget.businessId,
          outletName: widget.outletName,
        ),
        const SizedBox(height: 12),
        const ContentStatePanel(
          state: ContentState.preview,
          title: 'Forecast unavailable',
          message: 'No approved ERP forecast is connected. No historical values, predictions or demand rankings are loaded or estimated.',
        ),
        const SizedBox(height: 16),
        Text(
          'Forecast horizon',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 4),
        const Text(
          'One to three months follows the inspected prototype. Calendar-month boundaries and filters remain subject to approval.',
        ),
        const SizedBox(height: 8),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: SegmentedButton<ForecastHorizon>(
            segments: [
              for (final horizon in ForecastHorizon.values)
                ButtonSegment<ForecastHorizon>(
                  value: horizon,
                  label: Text(horizon.label),
                ),
            ],
            selected: {_horizon},
            showSelectedIcon: false,
            style: const ButtonStyle(
              minimumSize: WidgetStatePropertyAll(Size(96, 48)),
            ),
            onSelectionChanged: (selection) {
              setState(() => _horizon = selection.single);
            },
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Selected: ${_horizon.label}. Presentation only; no forecast calculation or request.',
          key: const ValueKey('selected-forecast-horizon'),
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 16),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: SegmentedButton<ForecastSection>(
            key: const ValueKey('forecast-section-selector'),
            segments: [
              for (final section in ForecastSection.values)
                ButtonSegment<ForecastSection>(
                  value: section,
                  label: Text(section.label),
                ),
            ],
            selected: {_section},
            showSelectedIcon: false,
            style: const ButtonStyle(
              minimumSize: WidgetStatePropertyAll(Size(112, 48)),
            ),
            onSelectionChanged: (selection) {
              setState(() => _section = selection.single);
            },
          ),
        ),
        const SizedBox(height: 16),
        switch (_section) {
          ForecastSection.overview => _overview(context),
          ForecastSection.explanation => _explanation(context),
          ForecastSection.recommendations => _recommendations(context),
        },
      ],
    );
  }

  Widget _overview(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        'Actual vs Predicted Sales',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const ForecastChartPresentation(points: []),
      const SizedBox(height: 20),
      Text('Demand outlook', style: Theme.of(context).textTheme.titleLarge),
      const SizedBox(height: 8),
      SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: SegmentedButton<ForecastDimension>(
          key: const ValueKey('forecast-dimension-selector'),
          segments: [
            for (final dimension in ForecastDimension.values)
              ButtonSegment<ForecastDimension>(
                value: dimension,
                label: Text(dimension.label),
              ),
          ],
          selected: {_dimension},
          showSelectedIcon: false,
          style: const ButtonStyle(
            minimumSize: WidgetStatePropertyAll(Size(88, 48)),
          ),
          onSelectionChanged: (selection) {
            setState(() => _dimension = selection.single);
          },
        ),
      ),
      const SizedBox(height: 8),
      Text(
        '${_dimension.label} demand is unavailable. No ranked products, variants, colours or sizes are fabricated.',
        key: const ValueKey('forecast-demand-state'),
      ),
      const SizedBox(height: 12),
      for (final dimension in ForecastDimension.values)
        _UnavailableDemandCard(dimension: dimension),
    ],
  );

  Widget _explanation(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        'Forecast explanation',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Forecast facts unavailable',
        message: 'Model, generation, training cutoff, quality and history coverage will be shown only when supplied by an approved ERP forecast.',
      ),
      const SizedBox(height: 12),
      ForecastMetadataPanel(metadata: null),
      const SizedBox(height: 12),
      _ExplanationStateCard(),
      const SizedBox(height: 12),
      const ContentStatePanel(
        state: ContentState.empty,
        title: 'Insufficient-data state',
        message: 'An approved service must report data coverage and uncertainty. Flutter will not invent confidence, precision or causal explanations.',
      ),
    ],
  );

  Widget _recommendations(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        'Production and replenishment recommendations',
        style: Theme.of(context).textTheme.titleLarge,
      ),
      const SizedBox(height: 8),
      const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Recommendation unavailable',
        message: 'Demand, available stock, incoming quantities, safety stock, lead time and suggested net quantity require an authorized ERP forecast.',
      ),
      const SizedBox(height: 12),
      const Text(
        'What-if inputs',
        style: TextStyle(fontWeight: FontWeight.w600),
      ),
      const SizedBox(height: 4),
      const Text(
        'Optional local scenario inputs only. They are not validated, calculated, saved or submitted.',
      ),
      const SizedBox(height: 12),
      _scenarioField(
        controller: _safetyStockController,
        label: 'Safety stock assumption',
      ),
      const SizedBox(height: 12),
      _scenarioField(
        controller: _leadTimeController,
        label: 'Lead-time assumption',
      ),
      const SizedBox(height: 12),
      _scenarioField(
        controller: _proposedUnitsController,
        label: 'Proposed units assumption',
      ),
      const SizedBox(height: 12),
      OutlinedButton.icon(
        onPressed: null,
        icon: Icon(Icons.lock_outline),
        label: Text('Simulation not connected'),
      ),
      const SizedBox(height: 8),
      const Text(
        'No purchase order, production request or stock adjustment can be created here.',
      ),
    ],
  );

  Widget _scenarioField({
    required TextEditingController controller,
    required String label,
  }) => TextField(
    controller: controller,
    keyboardType: const TextInputType.numberWithOptions(decimal: true),
    decoration: InputDecoration(
      labelText: label,
      hintText: 'Enter an assumption',
      border: const OutlineInputBorder(),
    ),
  );
}

final class ForecastChartPresentation extends StatelessWidget {
  const ForecastChartPresentation({required this.points, super.key});

  final List<ForecastChartPoint> points;

  @override
  Widget build(BuildContext context) {
    if (points.isEmpty) {
      return const ContentStatePanel(
        state: ContentState.unavailable,
        title: 'Chart unavailable',
        message: 'Observed history, model predictions and uncertainty bands appear only from an authorized forecast response.',
      );
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              height: 180,
              width: double.infinity,
              child: Semantics(
                container: true,
                label: _chartSemantics(points),
                child: ExcludeSemantics(
                  child: CustomPaint(
                    painter: _ForecastChartPainter(
                      points: points,
                      actualColor: Theme.of(context).colorScheme.primary,
                      predictionColor: Theme.of(context).colorScheme.tertiary,
                      bandColor: Theme.of(context).colorScheme.tertiary
                          .withValues(alpha: 0.16),
                    ),
                  ),
                ),
              ),
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 16,
              runSpacing: 8,
              children: [
                _LegendEntry(
                  label: 'Observed actual',
                  color: Theme.of(context).colorScheme.primary,
                ),
                _LegendEntry(
                  label: 'Model prediction',
                  color: Theme.of(context).colorScheme.tertiary,
                ),
                _LegendEntry(
                  label: 'Uncertainty range',
                  color: Theme.of(context).colorScheme.tertiary
                      .withValues(alpha: 0.45),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

final class _ScopeCard extends StatelessWidget {
  const _ScopeCard({required this.businessId, required this.outletName});

  final int businessId;
  final String? outletName;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Signed-in business',
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 4),
          Text('Business $businessId'),
          if (outletName != null) ...[
            const SizedBox(height: 8),
            const Text('Selected authorized outlet'),
            Text(outletName!),
          ],
          const SizedBox(height: 8),
          const Text(
            'Displayed scope is context only. It does not grant forecast access or change server-side authorization.',
          ),
        ],
      ),
    ),
  );
}

final class _UnavailableDemandCard extends StatelessWidget {
  const _UnavailableDemandCard({required this.dimension});

  final ForecastDimension dimension;

  @override
  Widget build(BuildContext context) => Card(
    child: ListTile(
      leading: const Icon(Icons.insights_outlined),
      title: Text('${dimension.label} demand'),
      subtitle: const Text('Unavailable · no forecast records are connected'),
      trailing: const Icon(Icons.lock_outline),
    ),
  );
}

final class ForecastMetadataPanel extends StatelessWidget {
  const ForecastMetadataPanel({required this.metadata, super.key});

  final ForecastMetadata? metadata;

  @override
  Widget build(BuildContext context) {
    final value = metadata;
    if (value == null) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Forecast provenance'),
              SizedBox(height: 8),
              Text('Generated: unavailable'),
              Text('Model version: unavailable'),
              Text('Training cutoff: unavailable'),
              Text('History coverage: unavailable'),
              Text('Data quality: unavailable'),
              Text('Fallback indicator: unavailable'),
            ],
          ),
        ),
      );
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Forecast provenance'),
            const SizedBox(height: 8),
            Text('Generated: ${_formatUtc(value.generatedAtUtc)}'),
            Text('Model version: ${value.modelVersion}'),
            Text('Training cutoff: ${_formatUtc(value.trainingCutoffUtc)}'),
            Text('History coverage: ${value.historyCoverageLabel}'),
            Text('Data quality: ${value.quality.label}'),
            Text('Coverage: ${value.coverage.label}'),
            Text('Fallback model: ${value.usesFallback ? 'Yes' : 'No'}'),
          ],
        ),
      ),
    );
  }
}

final class _ExplanationStateCard extends StatelessWidget {
  const _ExplanationStateCard();

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Optional AI explanation',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          const Text(
            'Not generated. If enabled later, narrative must be grounded in authorized forecast facts and remain separate from model measurements.',
          ),
          const SizedBox(height: 8),
          const Text(
            'No causal reason is inferred from historical patterns. Insufficient data must be reported by the forecast service.',
          ),
        ],
      ),
    ),
  );
}

final class _LegendEntry extends StatelessWidget {
  const _LegendEntry({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Semantics(
    label: label,
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(width: 16, height: 3, color: color),
        const SizedBox(width: 6),
        Text(label),
      ],
    ),
  );
}

final class _ForecastChartPainter extends CustomPainter {
  const _ForecastChartPainter({
    required this.points,
    required this.actualColor,
    required this.predictionColor,
    required this.bandColor,
  });

  final List<ForecastChartPoint> points;
  final Color actualColor;
  final Color predictionColor;
  final Color bandColor;

  @override
  void paint(Canvas canvas, Size size) {
    if (points.isEmpty || size.isEmpty) return;

    final values = [
      for (final point in points)
        for (final value in [
          point.actual,
          point.predicted,
          point.lowerBound,
          point.upperBound,
        ])
          ?value,
    ];
    if (values.isEmpty) return;

    final minValue = values.reduce((a, b) => a < b ? a : b);
    final maxValue = values.reduce((a, b) => a > b ? a : b);
    final range = maxValue - minValue;
    final safeRange = range == 0 ? 1.0 : range;
    final chart = Rect.fromLTWH(4, 4, size.width - 8, size.height - 8);
    Offset position(int index, double value) => Offset(
      chart.left +
          (points.length == 1
              ? chart.width / 2
              : chart.width * index / (points.length - 1)),
      chart.bottom - ((value - minValue) / safeRange * chart.height),
    );

    final bandIndexes = [
      for (var i = 0; i < points.length; i++)
        if (points[i].lowerBound != null && points[i].upperBound != null) i,
    ];
    if (bandIndexes.length >= 2) {
      final path = Path();
      for (var i = 0; i < bandIndexes.length; i++) {
        final index = bandIndexes[i];
        final point = points[index];
        final offset = position(index, point.upperBound!);
        if (i == 0) {
          path.moveTo(offset.dx, offset.dy);
        } else {
          path.lineTo(offset.dx, offset.dy);
        }
      }
      for (final index in bandIndexes.reversed) {
        final offset = position(index, points[index].lowerBound!);
        path.lineTo(offset.dx, offset.dy);
      }
      path.close();
      canvas.drawPath(
        path,
        Paint()
          ..color = bandColor
          ..style = PaintingStyle.fill,
      );
    }

    _drawSeries(canvas, position, (point) => point.actual, actualColor);
    _drawSeries(canvas, position, (point) => point.predicted, predictionColor);
  }

  void _drawSeries(
    Canvas canvas,
    Offset Function(int index, double value) position,
    double? Function(ForecastChartPoint point) valueFor,
    Color color,
  ) {
    final pointsWithValues = [
      for (var i = 0; i < points.length; i++)
        if (valueFor(points[i]) case final value?) (i, value),
    ];
    if (pointsWithValues.isEmpty) return;
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2.5
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;
    final path = Path();
    for (var i = 0; i < pointsWithValues.length; i++) {
      final (index, value) = pointsWithValues[i];
      final offset = position(index, value);
      if (i == 0) {
        path.moveTo(offset.dx, offset.dy);
      } else {
        path.lineTo(offset.dx, offset.dy);
      }
    }
    canvas.drawPath(path, paint);
    for (final (index, value) in pointsWithValues) {
      canvas.drawCircle(
        position(index, value),
        3.5,
        Paint()
          ..color = color
          ..style = PaintingStyle.fill,
      );
    }
  }

  @override
  bool shouldRepaint(covariant _ForecastChartPainter oldDelegate) =>
      oldDelegate.points != points ||
      oldDelegate.actualColor != actualColor ||
      oldDelegate.predictionColor != predictionColor ||
      oldDelegate.bandColor != bandColor;
}

String _chartSemantics(List<ForecastChartPoint> points) {
  final actualCount = points.where((point) => point.actual != null).length;
  final predictedCount = points
      .where((point) => point.predicted != null)
      .length;
  final uncertaintyCount = points
      .where((point) => point.lowerBound != null && point.upperBound != null)
      .length;
  return 'Forecast chart. $actualCount observed actual points, '
      '$predictedCount model prediction points, and '
      '$uncertaintyCount uncertainty ranges. Values are shown as provided; '
      'no predictions are calculated on this device.';
}

String _formatUtc(DateTime value) => value.toUtc().toIso8601String();
