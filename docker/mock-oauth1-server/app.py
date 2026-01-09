#!/usr/bin/env python3
"""
Mock OAuth1 Server for Testing TokenRelay
Validates OAuth1 signatures for integration testing.
"""

import os
import time
import uuid
from flask import Flask, request, jsonify
from oauth1_validator import validate_oauth1_request, parse_authorization_header

app = Flask(__name__)

# Load credentials from environment variables
def get_credentials():
    """Get OAuth1 credentials from environment variables."""
    signature_methods_str = os.environ.get('OAUTH1_SIGNATURE_METHOD', 'HMAC-SHA256,HMAC-SHA1')
    signature_methods = [m.strip() for m in signature_methods_str.split(',')]

    return {
        'consumer_key': os.environ.get('OAUTH1_CONSUMER_KEY', 'test-consumer-key'),
        'consumer_secret': os.environ.get('OAUTH1_CONSUMER_SECRET', 'test-consumer-secret'),
        'token_id': os.environ.get('OAUTH1_TOKEN_ID', 'test-token-id'),
        'token_secret': os.environ.get('OAUTH1_TOKEN_SECRET', 'test-token-secret'),
        'realm': os.environ.get('OAUTH1_REALM', 'test-realm'),
        'signature_methods': signature_methods
    }


def get_timestamp_tolerance():
    """Get timestamp tolerance from environment."""
    return int(os.environ.get('OAUTH1_TIMESTAMP_TOLERANCE', '300'))


def is_debug_mode():
    """Check if debug mode is enabled."""
    return os.environ.get('OAUTH1_DEBUG', 'false').lower() == 'true'


def validate_request():
    """
    Validate the OAuth1 signature on the current request.
    Returns (is_valid, result_dict)
    """
    auth_header = request.headers.get('Authorization', '')
    credentials = get_credentials()

    # Get the full URL as the client sees it
    # Use X-Forwarded headers if behind a proxy
    if request.headers.get('X-Forwarded-Proto'):
        scheme = request.headers.get('X-Forwarded-Proto')
        host = request.headers.get('X-Forwarded-Host', request.host)
        request_url = f'{scheme}://{host}{request.path}'
    else:
        request_url = request.url.split('?')[0]  # URL without query string

    # Add query string back if present
    if request.query_string:
        request_url += '?' + request.query_string.decode('utf-8')

    result = validate_oauth1_request(
        http_method=request.method,
        request_url=request_url,
        auth_header=auth_header,
        query_params=dict(request.args),
        credentials=credentials,
        timestamp_tolerance=get_timestamp_tolerance()
    )

    return result.get('valid', False), result


@app.route('/health', methods=['GET'])
def health():
    """Health check endpoint."""
    return jsonify({
        'status': 'healthy',
        'service': 'Mock OAuth1 Server',
        'version': '1.0.0',
        'timestamp': int(time.time())
    }), 200


@app.route('/oauth1/resource', methods=['GET', 'POST', 'PUT', 'DELETE', 'PATCH'])
def oauth1_resource():
    """
    Protected resource endpoint.
    Validates OAuth1 signature and returns success/failure.
    """
    is_valid, result = validate_request()

    if not is_valid:
        response = {
            'status': 'error',
            'error': result.get('error', 'unknown_error'),
            'message': result.get('message', 'OAuth1 signature validation failed')
        }

        # Include debug info if enabled
        if is_debug_mode() and 'debug' in result:
            response['debug'] = result['debug']

        return jsonify(response), 401

    return jsonify({
        'status': 'success',
        'message': 'OAuth1 signature valid',
        'oauth_params': result.get('oauth_params', {}),
        'request': {
            'method': request.method,
            'path': request.path,
            'query_params': dict(request.args)
        }
    }), 200


@app.route('/oauth1/echo', methods=['GET', 'POST', 'PUT', 'DELETE', 'PATCH'])
def oauth1_echo():
    """
    Echo endpoint that returns detailed request information.
    Validates OAuth1 signature and echoes back request details.
    """
    is_valid, result = validate_request()

    if not is_valid:
        response = {
            'status': 'error',
            'error': result.get('error', 'unknown_error'),
            'message': result.get('message', 'OAuth1 signature validation failed'),
            'oauth_params': result.get('oauth_params', {})
        }

        if is_debug_mode() and 'debug' in result:
            response['debug'] = result['debug']

        return jsonify(response), 401

    # Echo back full request details
    return jsonify({
        'status': 'success',
        'message': 'OAuth1 signature valid',
        'oauth_params': result.get('oauth_params', {}),
        'request': {
            'method': request.method,
            'path': request.path,
            'url': request.url,
            'query_params': dict(request.args),
            'headers': {k: v for k, v in request.headers if k.lower() != 'authorization'},
            'content_type': request.content_type,
            'content_length': request.content_length
        },
        'body_received': request.get_data(as_text=True) if request.content_length else None
    }), 200


@app.route('/oauth1/debug', methods=['GET', 'POST', 'PUT', 'DELETE', 'PATCH'])
def oauth1_debug():
    """
    Debug endpoint that always returns signature validation details.
    Useful for troubleshooting signature issues.
    """
    auth_header = request.headers.get('Authorization', '')
    credentials = get_credentials()

    # Parse OAuth params from header
    oauth_params = parse_authorization_header(auth_header)

    # Get the full URL
    if request.headers.get('X-Forwarded-Proto'):
        scheme = request.headers.get('X-Forwarded-Proto')
        host = request.headers.get('X-Forwarded-Host', request.host)
        request_url = f'{scheme}://{host}{request.path}'
    else:
        request_url = request.url.split('?')[0]

    if request.query_string:
        request_url += '?' + request.query_string.decode('utf-8')

    result = validate_oauth1_request(
        http_method=request.method,
        request_url=request_url,
        auth_header=auth_header,
        query_params=dict(request.args),
        credentials=credentials,
        timestamp_tolerance=get_timestamp_tolerance()
    )

    response = {
        'valid': result.get('valid', False),
        'error': result.get('error'),
        'message': result.get('message'),
        'oauth_params_received': oauth_params,
        'oauth_params_validated': result.get('oauth_params', {}),
        'request': {
            'method': request.method,
            'url': request_url,
            'query_params': dict(request.args)
        },
        'credentials_configured': {
            'consumer_key': credentials['consumer_key'],
            'token_id': credentials['token_id'],
            'realm': credentials['realm'],
            'signature_methods': credentials['signature_methods']
        }
    }

    # Always include debug info on this endpoint
    if 'debug' in result:
        response['signature_debug'] = result['debug']

    status_code = 200 if result.get('valid') else 401
    return jsonify(response), status_code


@app.route('/oauth1/data', methods=['GET', 'POST'])
def oauth1_data():
    """
    Mock data endpoint - returns sample data when OAuth1 is valid.
    Simulates a real API endpoint that would be protected by OAuth1.
    """
    is_valid, result = validate_request()

    if not is_valid:
        response = {
            'status': 'error',
            'error': result.get('error', 'unknown_error'),
            'message': result.get('message', 'OAuth1 signature validation failed')
        }
        return jsonify(response), 401

    # Return mock data like a real API would
    return jsonify({
        'status': 'success',
        'data': {
            'id': str(uuid.uuid4()),
            'name': 'Test Resource',
            'created_at': int(time.time()),
            'items': [
                {'id': 1, 'value': 'item_1'},
                {'id': 2, 'value': 'item_2'},
                {'id': 3, 'value': 'item_3'}
            ]
        },
        'authenticated_with': {
            'consumer_key': result.get('oauth_params', {}).get('consumer_key'),
            'token': result.get('oauth_params', {}).get('token'),
            'signature_method': result.get('oauth_params', {}).get('signature_method')
        }
    }), 200


@app.errorhandler(404)
def not_found(error):
    """Handle 404 errors."""
    return jsonify({
        'status': 'error',
        'error': 'not_found',
        'message': 'Endpoint not found'
    }), 404


@app.errorhandler(500)
def internal_error(error):
    """Handle 500 errors."""
    return jsonify({
        'status': 'error',
        'error': 'internal_error',
        'message': 'Internal server error'
    }), 500


if __name__ == '__main__':
    port = int(os.environ.get('PORT', 8081))
    debug = os.environ.get('FLASK_DEBUG', 'false').lower() == 'true'

    print(f'Starting Mock OAuth1 Server on port {port}')
    print(f'Debug mode: {is_debug_mode()}')
    print(f'Credentials configured:')
    creds = get_credentials()
    print(f'  Consumer Key: {creds["consumer_key"]}')
    print(f'  Token ID: {creds["token_id"]}')
    print(f'  Realm: {creds["realm"]}')
    print(f'  Signature Methods: {creds["signature_methods"]}')

    # Debug mode disabled for security
    app.run(host='0.0.0.0', port=port, debug=False)
