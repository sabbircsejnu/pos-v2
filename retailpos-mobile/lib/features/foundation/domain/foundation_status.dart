enum FoundationCapability {
  authentication,
  erpBusinessData,
  localCatalogue,
  stockTake,
}

class FoundationStatus {
  const FoundationStatus({
    required this.environment,
    required this.enabledCapabilities,
  });

  final String environment;
  final Set<FoundationCapability> enabledCapabilities;

  bool isEnabled(FoundationCapability capability) {
    return enabledCapabilities.contains(capability);
  }
}
