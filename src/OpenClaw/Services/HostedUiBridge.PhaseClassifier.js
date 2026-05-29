const openClawPhaseClassifier = (() => {
  const matchAny = (haystack, needles) => needles.find((needle) => haystack.includes(needle)) || '';

  const classify = ({ documentReadyState, hasBody, text, lowerUrl, strings, shellDetected }) => {
    const authMatch = matchAny(text, [
      'authentication required', 'authorization failed', 'unauthorized',
      'access denied', 'token required', 'password required',
      'session expired', 'sign in', 'log in', 'login required'
    ]);
    const tokenMissingMatch = matchAny(text, [
      'auth_token_missing', 'token missing', 'missing shared token'
    ]);
    const tokenMismatchMatch = matchAny(text, [
      'auth_token_mismatch', 'token mismatch', 'shared token did not match',
      'canretrywithdevicetoken'
    ]);
    const deviceTokenMismatchMatch = matchAny(text, [
      'auth_device_token_mismatch', 'device token mismatch',
      'cached per-device token is stale', 'stale or revoked device token'
    ]);
    const pairingMatch = matchAny(text, [
      'pairing required', 'pair this device', 'device approval required',
      'device not paired', 'disconnected (1008)'
    ]);
    const originMatch = matchAny(text, [
      'origin not allowed', 'origin rejected', 'allowed origins',
      'forbidden origin', 'trusted proxy'
    ]);
    const trustedProxyLoopbackMatch = matchAny(text, [
      'trusted_proxy_loopback_source', 'loopback-source trusted-proxy',
      'same-host loopback reverse proxies do not satisfy trusted-proxy auth',
      'same-host loopback reverse proxy', 'trusted-proxy auth rejects loopback-source requests'
    ]);
    const mixedTrustedProxyTokenMatch = matchAny(text, [
      'mixed_trusted_proxy_token', 'mixed token config',
      'both a gateway.auth.token', 'trusted-proxy mode are active at the same time',
      'remove the shared token when using trusted-proxy mode'
    ]);
    const trustedProxyIdentityHeaderMatch = matchAny(text, [
      'trusted_proxy_user_missing', 'trusted_proxy_user_not_allowed',
      'trustedproxy_missing_header', 'missing_header',
      'identity headers', 'required header wasn\'t present'
    ]);
    const trustedProxyOriginRejectedMatch = matchAny(text, [
      'trusted_proxy_origin_not_allowed', 'origin did not pass control ui origin checks'
    ]);
    const rateLimitMatch = matchAny(text, [
      'retry later', 'too many failed auth attempts', 'retry-after',
      'rate limited', 'rate limit'
    ]);
    const gatewayErrorMatch = matchAny(text, [
      'unable to connect', 'connection lost', 'gateway unavailable',
      'failed to connect', 'websocket closed', 'disconnect code'
    ]);
    const connectingMatch = matchAny(text, [
      'connecting to gateway', 'waiting for gateway',
      'reconnecting', 'establishing connection'
    ]);
    const isNonLocalHttp =
      lowerUrl.startsWith('http://') &&
      !/\/\/(?:127\.0\.0\.1|localhost|\[::1\])(?::|\/|$)/.test(lowerUrl);
    const insecureHttpMatch = matchAny(text, [
      'non-secure context', 'webcrypto', 'allowinsecureauth',
      'dangerouslydisabledeviceauth', 'device identity checks',
      'use https', 'tailscale serve'
    ]);

    let phase = 'page_loaded';
    let summary = strings.bridgeGatewayUiLoaded;
    let detail = '';

    if (!hasBody || documentReadyState === 'loading') {
      phase = 'loading';
      summary = strings.bridgePageLoading;
    } else if (tokenMissingMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTokenMissingSummary;
      detail = strings.bridgeTokenMissingDetail;
    } else if (tokenMismatchMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTokenMismatchSummary;
      detail = strings.bridgeTokenMismatchDetail;
    } else if (deviceTokenMismatchMatch) {
      phase = 'auth_required';
      summary = strings.bridgeDeviceTokenMismatchSummary;
      detail = strings.bridgeDeviceTokenMismatchDetail;
    } else if (originMatch) {
      phase = 'origin_rejected';
      summary = strings.bridgeOriginRejectedSummary;
      detail = strings.bridgeOriginRejectedDetail;
    } else if (trustedProxyLoopbackMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTrustedProxyLoopbackSummary;
      detail = strings.bridgeTrustedProxyLoopbackDetail;
    } else if (mixedTrustedProxyTokenMatch) {
      phase = 'auth_required';
      summary = strings.bridgeMixedAuthSummary;
      detail = strings.bridgeMixedAuthDetail;
    } else if (trustedProxyIdentityHeaderMatch) {
      phase = 'auth_required';
      summary = strings.bridgeTrustedProxyHeaderSummary;
      detail = strings.bridgeTrustedProxyHeaderDetail;
    } else if (trustedProxyOriginRejectedMatch) {
      phase = 'origin_rejected';
      summary = strings.bridgeTrustedProxyOriginSummary;
      detail = strings.bridgeTrustedProxyOriginDetail;
    } else if (rateLimitMatch) {
      phase = 'auth_required';
      summary = strings.bridgeRateLimitedSummary;
      detail = strings.bridgeRateLimitedDetail;
    } else if (isNonLocalHttp && insecureHttpMatch) {
      phase = 'gateway_error';
      summary = strings.bridgeInsecureHttpSummary;
      detail = strings.bridgeInsecureHttpDetail;
    } else if (pairingMatch) {
      phase = 'pairing_required';
      summary = strings.bridgePairingSummary;
      detail = strings.bridgePairingDetail;
    } else if (authMatch || /\/(login|signin|auth)(\/|$|\?)/.test(lowerUrl)) {
      phase = 'auth_required';
      summary = strings.bridgeAuthRequiredSummary;
      detail = strings.bridgeAuthRequiredDetail;
    } else if (gatewayErrorMatch) {
      phase = 'gateway_error';
      summary = strings.bridgeGatewaySessionNotConnectedSummary;
      detail = strings.bridgeGatewaySessionNotConnectedDetail;
    } else if (connectingMatch) {
      phase = 'gateway_connecting';
      summary = strings.bridgeConnectingSummary;
      detail = strings.bridgeConnectingDetail;
    } else if (shellDetected) {
      phase = 'connected';
      summary = strings.bridgeConnectedSummary;
    }

    return { phase, summary, detail };
  };

  const createClassifier = ({ dom, mutationFilter, strings }) => {
    const isStatusProbeExcludedElement = mutationFilter.isStatusProbeExcludedElement;

    const collectSignalText = () => {
      const selectors = [
        '[role="alert"]', '[role="status"]', '[aria-live]',
        '[data-status]', '[data-state]', '[data-busy]',
        '[class*="auth"]', '[class*="login"]', '[class*="signin"]',
        '[class*="error"]', '[class*="pair"]', '[class*="origin"]',
        '[class*="proxy"]', '[class*="connect"]', '[class*="disconnect"]'
      ];
      const fragments = [];
      const seen = new Set();
      let totalLength = 0;

      for (const selector of selectors) {
        for (const element of document.querySelectorAll(selector)) {
          if (isStatusProbeExcludedElement(element) || !dom.isVisible(element)) continue;
          const text = dom.compactText(dom.textOf(element)).toLowerCase();
          if (!text) continue;
          const normalized = text.length > 240 ? `${text.slice(0, 240)}...` : text;
          if (seen.has(normalized)) continue;
          seen.add(normalized);
          fragments.push(normalized);
          totalLength += normalized.length;
          if (fragments.length >= 6 || totalLength >= 900) break;
        }
        if (fragments.length >= 6 || totalLength >= 900) break;
      }

      return fragments.join(' ');
    };

    return {
      collectSignalText,
      classify: (input) => classify({ ...input, strings })
    };
  };

  return { classify, createClassifier };
})();
