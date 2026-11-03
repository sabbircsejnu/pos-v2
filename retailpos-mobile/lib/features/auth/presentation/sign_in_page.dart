import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../application/auth_session_controller.dart';
import '../domain/auth_failure.dart';

final class SignInPage extends ConsumerStatefulWidget {
  const SignInPage({super.key});

  @override
  ConsumerState<SignInPage> createState() => _SignInPageState();
}

final class _SignInPageState extends ConsumerState<SignInPage> {
  final _formKey = GlobalKey<FormState>();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(authSessionControllerProvider);
    final busy =
        state.status == AuthStatus.signingIn ||
        state.status == AuthStatus.restoring;

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            padding: const EdgeInsets.all(20),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Form(
                    key: _formKey,
                    child: AutofillGroup(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Center(
                            child: Image.asset(
                              'assets/brand/iranimart-logo.png',
                              width: 240,
                              height: 168,
                              fit: BoxFit.contain,
                              semanticLabel: 'IraniMart logo',
                            ),
                          ),
                          Text(
                            'Business',
                            style: Theme.of(context).textTheme.titleLarge,
                          ),
                          const SizedBox(height: 32),
                          Text(
                            'Welcome back',
                            style: Theme.of(context).textTheme.headlineMedium,
                          ),
                          const SizedBox(height: 8),
                          Text(
                            'Sign in with your RetailPOS ERP account. Your password is not saved on this device.',
                            style: Theme.of(context).textTheme.bodyLarge,
                          ),
                          if (state.failure != null) ...[
                            const SizedBox(height: 20),
                            _MessageCard(
                              message: _failureMessage(state.failure!),
                            ),
                          ],
                          if (state.remoteLogoutUnconfirmed) ...[
                            const SizedBox(height: 12),
                            const _MessageCard(
                              message: 'Signed out on this device; remote token revocation could not be confirmed.',
                            ),
                          ],
                          const SizedBox(height: 24),
                          TextFormField(
                            controller: _emailController,
                            keyboardType: TextInputType.emailAddress,
                            autofillHints: const [AutofillHints.username],
                            autocorrect: false,
                            textInputAction: TextInputAction.next,
                            decoration: const InputDecoration(
                              labelText: 'Email',
                              prefixIcon: Icon(Icons.email_outlined),
                            ),
                            validator: (value) =>
                                value == null || value.trim().isEmpty
                                ? 'Enter your email.'
                                : null,
                          ),
                          const SizedBox(height: 16),
                          TextFormField(
                            controller: _passwordController,
                            obscureText: true,
                            autocorrect: false,
                            autofillHints: const [AutofillHints.password],
                            textInputAction: TextInputAction.done,
                            onFieldSubmitted: (_) => _submit(),
                            decoration: const InputDecoration(
                              labelText: 'Password',
                              prefixIcon: Icon(Icons.lock_outline),
                            ),
                            validator: (value) => value == null || value.isEmpty
                                ? 'Enter your password.'
                                : null,
                          ),
                          const SizedBox(height: 20),
                          FilledButton(
                            onPressed: busy ? null : _submit,
                            child: busy
                                ? const SizedBox.square(
                                    dimension: 18,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Text('Sign in'),
                          ),
                          if (state.failure == AuthFailureKind.offline) ...[
                            const SizedBox(height: 8),
                            TextButton(
                              onPressed: busy
                                  ? null
                                  : ref
                                        .read(
                                          authSessionControllerProvider
                                              .notifier,
                                        )
                                        .restore,
                              child: const Text(
                                'Try restoring the saved session',
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  void _submit() {
    if (!_formKey.currentState!.validate()) return;
    ref
        .read(authSessionControllerProvider.notifier)
        .signIn(
          email: _emailController.text,
          password: _passwordController.text,
        );
  }

  static String _failureMessage(AuthFailureKind kind) => switch (kind) {
    AuthFailureKind.invalidCredentials => 'Email or password is incorrect.',
    AuthFailureKind.passwordSetupRequired =>
      'Password setup is required. Complete your invitation before signing in.',
    AuthFailureKind.unauthorized => 'Please sign in to continue.',
    AuthFailureKind.forbidden =>
      'This account is not authorized for the mobile app.',
    AuthFailureKind.rateLimited =>
      'Too many attempts. Wait a moment and try again.',
    AuthFailureKind.offline =>
      'Unable to reach ERP. Check your connection and try again.',
    AuthFailureKind.server =>
      'ERP could not complete the request. Try again later.',
    AuthFailureKind.configuration =>
      'An HTTPS ERP address is not configured for this build.',
    AuthFailureKind.secureStorage =>
      'Secure storage is unavailable. Sign-in cannot continue safely.',
    AuthFailureKind.invalidServerResponse =>
      'ERP returned an unexpected authentication response.',
    AuthFailureKind.unsupportedAccount =>
      'This account or session type is not supported in the mobile app.',
    AuthFailureKind.sessionEnded =>
      'Your session ended or could not be securely renewed. Sign in again.',
  };
}

final class _MessageCard extends StatelessWidget {
  const _MessageCard({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) => Card(
    color: Theme.of(context).colorScheme.errorContainer,
    child: Padding(
      padding: const EdgeInsets.all(12),
      child: Semantics(liveRegion: true, child: Text(message)),
    ),
  );
}
