#!/usr/bin/env python3
"""
OAuth1 Signature Validator
Implements RFC 5849 OAuth 1.0 signature validation.
This mirrors the signature generation logic in TokenRelay/Services/OAuth1Service.cs
"""

import hmac
import hashlib
import base64
import time
import re
from urllib.parse import urlparse, parse_qs, quote, unquote


# Characters that should NOT be encoded per RFC 3986
UNRESERVED_CHARS = set('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~')


def percent_encode(value: str) -> str:
    """
    Percent encodes a string per RFC 3986.
    Mirrors OAuth1Service.PercentEncode() (lines 319-345)
    """
    if not value:
        return ''

    encoded = []
    for char in value:
        if char in UNRESERVED_CHARS:
            encoded.append(char)
        else:
            for byte in char.encode('utf-8'):
                encoded.append(f'%{byte:02X}')

    return ''.join(encoded)


def normalize_url(url: str) -> str:
    """
    Normalizes the request URL per RFC 5849 Section 3.4.1.2.
    Mirrors OAuth1Service.NormalizeRequestUrl() (lines 245-263)

    - Scheme and host are lowercase
    - Default ports (80 for http, 443 for https) are removed
    - Query string is removed (handled separately)
    """
    parsed = urlparse(url)

    scheme = parsed.scheme.lower()
    host = parsed.hostname.lower() if parsed.hostname else ''
    port = parsed.port
    path = parsed.path or '/'

    # Remove default ports
    include_port = True
    if (scheme == 'http' and port == 80) or (scheme == 'https' and port == 443):
        include_port = False
    elif port is None:
        include_port = False

    normalized = f'{scheme}://{host}'
    if include_port and port:
        normalized += f':{port}'
    normalized += path

    return normalized


def parse_authorization_header(auth_header: str) -> dict:
    """
    Parses the OAuth Authorization header into a dictionary.

    Format: OAuth realm="...", oauth_consumer_key="...", ...
    """
    if not auth_header or not auth_header.startswith('OAuth '):
        return {}

    params = {}
    # Remove 'OAuth ' prefix
    header_content = auth_header[6:]

    # Parse key="value" pairs
    # Match: key="value" (handles escaped quotes)
    pattern = r'(\w+)="([^"]*)"'
    matches = re.findall(pattern, header_content)

    for key, value in matches:
        # URL decode the value
        params[key] = unquote(value)

    return params


def generate_signature_base_string(http_method: str, base_url: str, params: dict) -> str:
    """
    Generates the signature base string per RFC 5849 Section 3.4.1.
    Mirrors OAuth1Service.GenerateSignatureBaseString() (lines 269-284)

    Format: HTTP_METHOD&NORMALIZED_URL&NORMALIZED_PARAMS
    """
    # Sort parameters alphabetically by key, then by value
    sorted_params = sorted(params.items(), key=lambda x: (x[0], x[1]))

    # Encode and join parameters
    normalized_params = '&'.join(
        f'{percent_encode(k)}={percent_encode(v)}'
        for k, v in sorted_params
    )

    return f'{http_method.upper()}&{percent_encode(base_url)}&{percent_encode(normalized_params)}'


def generate_signature(base_string: str, consumer_secret: str, token_secret: str,
                       signature_method: str = 'HMAC-SHA256') -> str:
    """
    Generates the OAuth signature using the specified method.
    Mirrors OAuth1Service.GenerateSignature() (lines 289-314)
    """
    # Construct the signing key: consumer_secret&token_secret
    signing_key = f'{percent_encode(consumer_secret)}&{percent_encode(token_secret)}'
    key_bytes = signing_key.encode('utf-8')
    data_bytes = base_string.encode('utf-8')

    if signature_method == 'HMAC-SHA256':
        hash_bytes = hmac.new(key_bytes, data_bytes, hashlib.sha256).digest()
    elif signature_method == 'HMAC-SHA1':
        hash_bytes = hmac.new(key_bytes, data_bytes, hashlib.sha1).digest()
    else:
        raise ValueError(f'Unsupported signature method: {signature_method}')

    return base64.b64encode(hash_bytes).decode('utf-8')


def validate_oauth1_request(
    http_method: str,
    request_url: str,
    auth_header: str,
    query_params: dict,
    credentials: dict,
    timestamp_tolerance: int = 300
) -> dict:
    """
    Validates an OAuth1 request.

    Args:
        http_method: HTTP method (GET, POST, etc.)
        request_url: Full request URL
        auth_header: Authorization header value
        query_params: Query string parameters from the request
        credentials: Dict with consumer_key, consumer_secret, token_id, token_secret, realm
        timestamp_tolerance: Allowed timestamp drift in seconds (default 5 minutes)

    Returns:
        dict with 'valid' (bool), 'error' (str if invalid), 'oauth_params' (dict)
    """
    # Parse Authorization header
    oauth_params = parse_authorization_header(auth_header)

    if not oauth_params:
        return {
            'valid': False,
            'error': 'missing_authorization',
            'message': 'Missing or invalid OAuth Authorization header'
        }

    # Extract OAuth parameters
    consumer_key = oauth_params.get('oauth_consumer_key')
    token = oauth_params.get('oauth_token')
    signature_method = oauth_params.get('oauth_signature_method', 'HMAC-SHA256')
    timestamp = oauth_params.get('oauth_timestamp')
    nonce = oauth_params.get('oauth_nonce')
    signature = oauth_params.get('oauth_signature')
    realm = oauth_params.get('realm')
    version = oauth_params.get('oauth_version', '1.0')

    # Validate required parameters
    if not all([consumer_key, token, timestamp, nonce, signature]):
        missing = []
        if not consumer_key: missing.append('oauth_consumer_key')
        if not token: missing.append('oauth_token')
        if not timestamp: missing.append('oauth_timestamp')
        if not nonce: missing.append('oauth_nonce')
        if not signature: missing.append('oauth_signature')

        return {
            'valid': False,
            'error': 'missing_parameters',
            'message': f'Missing required OAuth parameters: {", ".join(missing)}',
            'oauth_params': oauth_params
        }

    # Validate consumer key
    if consumer_key != credentials.get('consumer_key'):
        return {
            'valid': False,
            'error': 'invalid_consumer_key',
            'message': 'Consumer key does not match',
            'oauth_params': oauth_params
        }

    # Validate token
    if token != credentials.get('token_id'):
        return {
            'valid': False,
            'error': 'invalid_token',
            'message': 'Token does not match',
            'oauth_params': oauth_params
        }

    # Validate realm (if configured)
    expected_realm = credentials.get('realm')
    if expected_realm and realm != expected_realm:
        return {
            'valid': False,
            'error': 'invalid_realm',
            'message': f'Realm does not match. Expected: {expected_realm}, Got: {realm}',
            'oauth_params': oauth_params
        }

    # Validate signature method
    allowed_methods = credentials.get('signature_methods', ['HMAC-SHA256', 'HMAC-SHA1'])
    if signature_method not in allowed_methods:
        return {
            'valid': False,
            'error': 'invalid_signature_method',
            'message': f'Signature method not allowed: {signature_method}',
            'oauth_params': oauth_params
        }

    # Validate timestamp (within tolerance)
    try:
        request_timestamp = int(timestamp)
        current_timestamp = int(time.time())
        if abs(current_timestamp - request_timestamp) > timestamp_tolerance:
            return {
                'valid': False,
                'error': 'expired_timestamp',
                'message': f'Timestamp out of range. Current: {current_timestamp}, Request: {request_timestamp}',
                'oauth_params': oauth_params
            }
    except ValueError:
        return {
            'valid': False,
            'error': 'invalid_timestamp',
            'message': 'Timestamp is not a valid integer',
            'oauth_params': oauth_params
        }

    # Build parameters for signature (oauth params + query params)
    # Note: realm and oauth_signature are NOT included in signature base string
    sig_params = {
        'oauth_consumer_key': consumer_key,
        'oauth_token': token,
        'oauth_signature_method': signature_method,
        'oauth_timestamp': timestamp,
        'oauth_nonce': nonce,
        'oauth_version': version
    }

    # Add query parameters
    for key, value in query_params.items():
        if isinstance(value, list):
            value = value[0]  # Take first value if multiple
        sig_params[key] = value

    # Normalize URL (remove query string)
    base_url = normalize_url(request_url)

    # Generate expected signature
    base_string = generate_signature_base_string(http_method, base_url, sig_params)
    expected_signature = generate_signature(
        base_string,
        credentials.get('consumer_secret', ''),
        credentials.get('token_secret', ''),
        signature_method
    )

    # Compare signatures
    if signature != expected_signature:
        return {
            'valid': False,
            'error': 'invalid_signature',
            'message': 'Signature does not match',
            'oauth_params': oauth_params,
            'debug': {
                'expected_signature': expected_signature,
                'received_signature': signature,
                'signature_base_string': base_string,
                'signing_params': sig_params
            }
        }

    return {
        'valid': True,
        'oauth_params': {
            'consumer_key': consumer_key,
            'token': token,
            'signature_method': signature_method,
            'timestamp': timestamp,
            'nonce': nonce,
            'realm': realm,
            'version': version
        }
    }
