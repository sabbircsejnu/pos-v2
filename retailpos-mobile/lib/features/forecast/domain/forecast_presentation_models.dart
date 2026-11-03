enum ForecastHorizon {
  oneMonth(1, '1 month'),
  twoMonths(2, '2 months'),
  threeMonths(3, '3 months');

  const ForecastHorizon(this.months, this.label);

  final int months;
  final String label;
}

enum ForecastDimension {
  category('Category'),
  product('Product'),
  colour('Colour'),
  size('Size');

  const ForecastDimension(this.label);

  final String label;
}

enum ForecastCoverageState {
  available('Coverage unavailable'),
  sufficient('Coverage assessed'),
  insufficient('Insufficient data');

  const ForecastCoverageState(this.label);

  final String label;
}

enum ForecastQualityState {
  unavailable('Quality not reported'),
  validated('Validated'),
  fallback('Fallback model'),
  insufficient('Insufficient data');

  const ForecastQualityState(this.label);

  final String label;
}

final class ForecastMetadata {
  const ForecastMetadata({
    required this.generatedAtUtc,
    required this.modelVersion,
    required this.trainingCutoffUtc,
    required this.historyCoverageLabel,
    required this.quality,
    required this.coverage,
    required this.usesFallback,
  });

  final DateTime generatedAtUtc;
  final String modelVersion;
  final DateTime trainingCutoffUtc;
  final String historyCoverageLabel;
  final ForecastQualityState quality;
  final ForecastCoverageState coverage;
  final bool usesFallback;
}

final class ForecastChartPoint {
  const ForecastChartPoint({
    required this.periodStartUtc,
    this.actual,
    this.predicted,
    this.lowerBound,
    this.upperBound,
  });

  final DateTime periodStartUtc;
  final double? actual;
  final double? predicted;
  final double? lowerBound;
  final double? upperBound;
}
