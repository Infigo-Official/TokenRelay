#!/usr/bin/env python3
"""
Simple Mock OAuth 2.0 Server for Testing TokenRelay
Supports: Password Grant, Client Credentials Grant
"""

from flask import Flask, request, jsonify
import base64
import time
import uuid

app = Flask(__name__)

# Mock credentials
CLIENTS = {
    "test_client_1": "test_secret_1"
}

USERS = {
    "test_user": "test_password"
}

# In-memory token storage
tokens = {}

def validate_client_credentials():
    """
    Validate client credentials from either:
    1. HTTP Basic Authentication header (preferred)
    2. Form body parameters (for password grant with confidential clients)
    """
    # First try Basic Auth header
    auth_header = request.headers.get('Authorization')
    if auth_header and auth_header.startswith('Basic '):
        try:
            credentials = base64.b64decode(auth_header[6:]).decode('utf-8')
            client_id, client_secret = credentials.split(':', 1)

            if client_id in CLIENTS and CLIENTS[client_id] == client_secret:
                return client_id, client_secret
        except:
            pass

    # Fall back to form body parameters (common for password grant)
    client_id = request.form.get('client_id')
    client_secret = request.form.get('client_secret')

    if client_id and client_secret:
        if client_id in CLIENTS and CLIENTS[client_id] == client_secret:
            return client_id, client_secret

    return None, None

@app.route('/health', methods=['GET'])
def health():
    """Health check endpoint"""
    return jsonify({
        "status": "healthy",
        "service": "Mock OAuth Server",
        "version": "1.0.0"
    }), 200

@app.route('/v1/oauth/tokens', methods=['POST'])
def token():
    """OAuth 2.0 Token Endpoint"""

    # Validate client credentials via Basic Auth or form body
    client_id, client_secret = validate_client_credentials()

    if not client_id:
        return jsonify({
            "error": "invalid_client",
            "error_description": "Client authentication failed"
        }), 401

    # Get grant type
    grant_type = request.form.get('grant_type')

    if not grant_type:
        return jsonify({
            "error": "invalid_request",
            "error_description": "grant_type is required"
        }), 400

    # Handle Password Grant
    if grant_type == 'password':
        username = request.form.get('username')
        password = request.form.get('password')
        scope = request.form.get('scope', 'read write')

        if not username or not password:
            return jsonify({
                "error": "invalid_request",
                "error_description": "username and password are required"
            }), 400

        if username not in USERS or USERS[username] != password:
            return jsonify({
                "error": "invalid_grant",
                "error_description": "Invalid username or password"
            }), 400

        # Generate tokens
        access_token = f"mock_access_token_{uuid.uuid4().hex}"
        refresh_token = f"mock_refresh_token_{uuid.uuid4().hex}"

        # Store token
        tokens[access_token] = {
            "client_id": client_id,
            "username": username,
            "scope": scope,
            "created_at": time.time(),
            "expires_in": 3600
        }

        return jsonify({
            "access_token": access_token,
            "token_type": "Bearer",
            "expires_in": 3600,
            "refresh_token": refresh_token,
            "scope": scope
        }), 200

    # Handle Client Credentials Grant
    elif grant_type == 'client_credentials':
        scope = request.form.get('scope', 'read')

        # Generate token
        access_token = f"mock_access_token_{uuid.uuid4().hex}"

        # Store token
        tokens[access_token] = {
            "client_id": client_id,
            "scope": scope,
            "created_at": time.time(),
            "expires_in": 3600
        }

        return jsonify({
            "access_token": access_token,
            "token_type": "Bearer",
            "expires_in": 3600,
            "scope": scope
        }), 200

    else:
        return jsonify({
            "error": "unsupported_grant_type",
            "error_description": f"Grant type '{grant_type}' is not supported"
        }), 400

@app.route('/v1/users', methods=['GET'])
def get_users():
    """Mock API endpoint - requires valid token"""

    auth_header = request.headers.get('Authorization')
    if not auth_header or not auth_header.startswith('Bearer '):
        return jsonify({
            "error": "unauthorized",
            "error_description": "Missing or invalid authorization header"
        }), 401

    token = auth_header[7:]

    if token not in tokens:
        return jsonify({
            "error": "invalid_token",
            "error_description": "Token is invalid or expired"
        }), 401

    token_info = tokens[token]

    # Check if token is expired
    if time.time() - token_info['created_at'] > token_info['expires_in']:
        return jsonify({
            "error": "token_expired",
            "error_description": "Token has expired"
        }), 401

    # Return mock data
    return jsonify({
        "users": [
            {"id": 1, "username": "test_user", "email": "test@example.com"},
            {"id": 2, "username": "admin", "email": "admin@example.com"}
        ],
        "authenticated_as": token_info.get('username', token_info.get('client_id')),
        "scope": token_info['scope']
    }), 200

@app.route('/v1/data', methods=['GET'])
def get_data():
    """Another mock API endpoint - requires valid token"""

    auth_header = request.headers.get('Authorization')
    if not auth_header or not auth_header.startswith('Bearer '):
        return jsonify({
            "error": "unauthorized",
            "error_description": "Missing or invalid authorization header"
        }), 401

    token = auth_header[7:]

    if token not in tokens:
        return jsonify({
            "error": "invalid_token",
            "error_description": "Token is invalid or expired"
        }), 401

    token_info = tokens[token]

    # Check if token is expired
    if time.time() - token_info['created_at'] > token_info['expires_in']:
        return jsonify({
            "error": "token_expired",
            "error_description": "Token has expired"
        }), 401

    # Return mock data
    return jsonify({
        "message": "OAuth authentication successful!",
        "data": {
            "timestamp": time.time(),
            "random_value": uuid.uuid4().hex
        },
        "authenticated_as": token_info.get('username', token_info.get('client_id')),
        "token_valid": True
    }), 200


@app.route('/v1/echo', methods=['GET', 'POST', 'PUT', 'DELETE', 'PATCH'])
def echo():
    """
    Echo endpoint that returns request details including all headers.
    Used for testing:
    - Static auth header injection
    - Custom header forwarding
    - TOKEN-RELAY-* header exclusion
    - Request body preservation

    Does NOT require authentication - allows testing static auth scenarios.
    """
    auth_header = request.headers.get('Authorization')

    # Convert headers to dict (excluding some internal Flask headers)
    headers_dict = {}
    for key, value in request.headers:
        headers_dict[key] = value

    # Get the raw body data - use get_data with cache=True to allow re-reading
    # This handles both JSON and form-urlencoded bodies
    body_data = request.get_data(as_text=True, cache=True)

    return jsonify({
        "status": "success",
        "method": request.method,
        "path": request.path,
        "url": request.url,
        "headers": headers_dict,
        "body": body_data if body_data else None,
        "content_type": request.content_type,
        "content_length": request.content_length,
        "authorization_received": auth_header,
        "token_relay_auth_present": "Token-Relay-Auth" in request.headers or "TOKEN-RELAY-AUTH" in request.headers,
        "token_relay_target_present": "Token-Relay-Target" in request.headers or "TOKEN-RELAY-TARGET" in request.headers,
        "query_params": dict(request.args),
        "timestamp": time.time()
    }), 200


@app.route('/v1/status/<int:code>', methods=['GET', 'POST', 'PUT', 'DELETE', 'PATCH'])
def status_code(code):
    """
    Return specified HTTP status code for error testing.
    Used for testing error passthrough scenarios.

    Examples:
    - /v1/status/400 - Returns 400 Bad Request
    - /v1/status/404 - Returns 404 Not Found
    - /v1/status/500 - Returns 500 Internal Server Error
    """
    messages = {
        200: "OK",
        201: "Created",
        204: "No Content",
        400: "Bad Request",
        401: "Unauthorized",
        403: "Forbidden",
        404: "Not Found",
        405: "Method Not Allowed",
        500: "Internal Server Error",
        502: "Bad Gateway",
        503: "Service Unavailable"
    }

    message = messages.get(code, f"Status {code}")

    # Return empty body for 204
    if code == 204:
        return '', 204

    return jsonify({
        "status": code,
        "message": message,
        "method": request.method,
        "path": request.path
    }), code


# ============================================================================
# CRITICAL TEST ENDPOINTS - For testing OAuth2 edge cases and security
# ============================================================================

@app.route('/v1/oauth/inspect-auth', methods=['POST'])
def inspect_auth():
    """
    Token endpoint that inspects and returns how client authentication was sent.
    Used for testing Basic Auth vs form body credential sending.

    Returns details about:
    - Whether Basic Auth header was present
    - What the decoded Basic Auth credentials were
    - What form body credentials were sent
    """
    auth_header = request.headers.get('Authorization', '')
    basic_auth_present = auth_header.startswith('Basic ')

    basic_auth_client_id = None
    basic_auth_client_secret = None

    if basic_auth_present:
        try:
            credentials = base64.b64decode(auth_header[6:]).decode('utf-8')
            basic_auth_client_id, basic_auth_client_secret = credentials.split(':', 1)
        except:
            pass

    form_client_id = request.form.get('client_id')
    form_client_secret = request.form.get('client_secret')

    # Validate credentials from either source
    client_id, client_secret = validate_client_credentials()

    if not client_id:
        return jsonify({
            "error": "invalid_client",
            "error_description": "Client authentication failed",
            "auth_inspection": {
                "basic_auth_present": basic_auth_present,
                "basic_auth_client_id": basic_auth_client_id,
                "form_client_id": form_client_id,
                "form_client_secret_present": form_client_secret is not None
            }
        }), 401

    # Generate token and return with auth inspection info
    access_token = f"mock_access_token_{uuid.uuid4().hex}"

    return jsonify({
        "access_token": access_token,
        "token_type": "Bearer",
        "expires_in": 3600,
        "auth_inspection": {
            "basic_auth_present": basic_auth_present,
            "basic_auth_client_id": basic_auth_client_id,
            "basic_auth_client_secret_present": basic_auth_client_secret is not None,
            "form_client_id": form_client_id,
            "form_client_secret_present": form_client_secret is not None,
            "authenticated_via": "basic_auth" if basic_auth_present and basic_auth_client_id else "form_body"
        }
    }), 200


@app.route('/v1/oauth/malformed', methods=['POST'])
def malformed_token():
    """
    Returns a malformed (non-JSON) response to test error handling.
    Simulates an error page or HTML response from a misconfigured server.
    """
    return "<!DOCTYPE html><html><head><title>Error</title></head><body><h1>500 Internal Server Error</h1></body></html>", 200, {'Content-Type': 'text/html'}


@app.route('/v1/oauth/empty-token', methods=['POST'])
def empty_token():
    """
    Returns a response with an empty access_token string.
    Used for testing empty token validation.
    """
    return jsonify({
        "access_token": "",
        "token_type": "Bearer",
        "expires_in": 3600
    }), 200


@app.route('/v1/oauth/null-token', methods=['POST'])
def null_token():
    """
    Returns a response with null access_token.
    Used for testing null token validation.
    """
    return jsonify({
        "access_token": None,
        "token_type": "Bearer",
        "expires_in": 3600
    }), 200


@app.route('/v1/oauth/missing-token', methods=['POST'])
def missing_token():
    """
    Returns a response without the access_token field.
    Used for testing missing field validation.
    """
    return jsonify({
        "token_type": "Bearer",
        "expires_in": 3600,
        "scope": "read write"
    }), 200


@app.route('/v1/oauth/custom-response', methods=['POST'])
def custom_token_response():
    """
    Returns a customized token response based on query parameters.
    Used for testing various token response edge cases.

    Query params:
    - token_type: Override token_type (default: Bearer)
    - expires_in: Override expires_in (default: 3600)
    - omit_token_type: If 'true', omit token_type from response
    - omit_expires_in: If 'true', omit expires_in from response
    """
    # Validate client credentials first
    client_id, client_secret = validate_client_credentials()
    if not client_id:
        return jsonify({
            "error": "invalid_client",
            "error_description": "Client authentication failed"
        }), 401

    access_token = f"mock_access_token_{uuid.uuid4().hex}"

    response = {"access_token": access_token}

    # Handle token_type
    if request.args.get('omit_token_type') != 'true':
        token_type = request.args.get('token_type', 'Bearer')
        response["token_type"] = token_type

    # Handle expires_in
    if request.args.get('omit_expires_in') != 'true':
        try:
            expires_in = int(request.args.get('expires_in', 3600))
        except ValueError:
            expires_in = 3600
        response["expires_in"] = expires_in

    return jsonify(response), 200


@app.route('/v1/oauth/slow', methods=['POST'])
def slow_token():
    """
    Returns a token response after a delay.
    Used for testing timeout handling.

    Query params:
    - delay: Delay in seconds (default: 35, exceeds typical 30s timeout)
    """
    import time as time_module

    delay = int(request.args.get('delay', 35))
    time_module.sleep(delay)

    # Validate client credentials
    client_id, client_secret = validate_client_credentials()
    if not client_id:
        return jsonify({
            "error": "invalid_client",
            "error_description": "Client authentication failed"
        }), 401

    access_token = f"mock_access_token_{uuid.uuid4().hex}"

    return jsonify({
        "access_token": access_token,
        "token_type": "Bearer",
        "expires_in": 3600
    }), 200


@app.route('/v1/oauth/error/<int:status_code>', methods=['POST'])
def oauth_error(status_code):
    """
    Returns specified HTTP error status code from token endpoint.
    Used for testing HTTP error handling in token acquisition.

    Examples:
    - /v1/oauth/error/403 - Returns 403 Forbidden
    - /v1/oauth/error/500 - Returns 500 Internal Server Error
    - /v1/oauth/error/502 - Returns 502 Bad Gateway
    - /v1/oauth/error/503 - Returns 503 Service Unavailable
    """
    error_messages = {
        400: ("invalid_request", "Bad Request"),
        401: ("invalid_client", "Unauthorized"),
        403: ("access_denied", "Forbidden - Access denied"),
        404: ("not_found", "Token endpoint not found"),
        500: ("server_error", "Internal server error"),
        502: ("bad_gateway", "Bad gateway"),
        503: ("temporarily_unavailable", "Service temporarily unavailable")
    }

    error, description = error_messages.get(status_code, ("unknown_error", f"Error {status_code}"))

    return jsonify({
        "error": error,
        "error_description": description
    }), status_code


@app.route('/v1/oauth/tokens-strict', methods=['POST'])
def token_strict():
    """
    Strict OAuth 2.0 Token Endpoint that validates all grant type parameters.
    Used for testing grant type validation.
    """
    # Validate client credentials first
    client_id, client_secret = validate_client_credentials()

    if not client_id:
        return jsonify({
            "error": "invalid_client",
            "error_description": "Client authentication failed"
        }), 401

    grant_type = request.form.get('grant_type')

    if not grant_type:
        return jsonify({
            "error": "invalid_request",
            "error_description": "grant_type is required"
        }), 400

    # Strict validation for password grant
    if grant_type == 'password':
        username = request.form.get('username')
        password = request.form.get('password')

        if not username or username.strip() == '':
            return jsonify({
                "error": "invalid_request",
                "error_description": "username is required and cannot be empty"
            }), 400

        if not password or password.strip() == '':
            return jsonify({
                "error": "invalid_request",
                "error_description": "password is required and cannot be empty"
            }), 400

        if username not in USERS or USERS[username] != password:
            return jsonify({
                "error": "invalid_grant",
                "error_description": "Invalid username or password"
            }), 400

        access_token = f"mock_access_token_{uuid.uuid4().hex}"
        return jsonify({
            "access_token": access_token,
            "token_type": "Bearer",
            "expires_in": 3600
        }), 200

    # Strict validation for client_credentials grant
    elif grant_type == 'client_credentials':
        # client_id and client_secret already validated
        access_token = f"mock_access_token_{uuid.uuid4().hex}"
        return jsonify({
            "access_token": access_token,
            "token_type": "Bearer",
            "expires_in": 3600
        }), 200

    # Strict validation for authorization_code grant
    elif grant_type == 'authorization_code':
        code = request.form.get('code')
        redirect_uri = request.form.get('redirect_uri')

        if not code or code.strip() == '':
            return jsonify({
                "error": "invalid_request",
                "error_description": "code is required for authorization_code grant"
            }), 400

        if not redirect_uri or redirect_uri.strip() == '':
            return jsonify({
                "error": "invalid_request",
                "error_description": "redirect_uri is required for authorization_code grant"
            }), 400

        # For testing, accept any code
        access_token = f"mock_access_token_{uuid.uuid4().hex}"
        return jsonify({
            "access_token": access_token,
            "token_type": "Bearer",
            "expires_in": 3600
        }), 200

    # Strict validation for refresh_token grant
    elif grant_type == 'refresh_token':
        refresh_token = request.form.get('refresh_token')

        if not refresh_token or refresh_token.strip() == '':
            return jsonify({
                "error": "invalid_request",
                "error_description": "refresh_token is required for refresh_token grant"
            }), 400

        # For testing, accept any refresh token
        access_token = f"mock_access_token_{uuid.uuid4().hex}"
        new_refresh_token = f"mock_refresh_token_{uuid.uuid4().hex}"
        return jsonify({
            "access_token": access_token,
            "token_type": "Bearer",
            "expires_in": 3600,
            "refresh_token": new_refresh_token
        }), 200

    else:
        return jsonify({
            "error": "unsupported_grant_type",
            "error_description": f"Grant type '{grant_type}' is not supported"
        }), 400


@app.route('/v1/oauth/tokens-short-expiry', methods=['POST'])
def token_short_expiry():
    """
    Token endpoint that returns tokens with very short expiry.
    Used for testing token expiration and refresh.

    Query params:
    - expires_in: Token expiry in seconds (default: 2)
    """
    client_id, client_secret = validate_client_credentials()

    if not client_id:
        return jsonify({
            "error": "invalid_client",
            "error_description": "Client authentication failed"
        }), 401

    expires_in = int(request.args.get('expires_in', 2))
    access_token = f"mock_short_token_{uuid.uuid4().hex}"

    return jsonify({
        "access_token": access_token,
        "token_type": "Bearer",
        "expires_in": expires_in
    }), 200


# ============================================================================
# FILE DOWNLOAD TEST ENDPOINTS - For testing Downloader plugin
# ============================================================================

@app.route('/v1/files/sample.txt', methods=['GET'])
def file_sample_text():
    """Returns a plain text file for Downloader testing."""
    content = "Hello from the mock file server!\nThis is a sample text file for testing."
    response = app.make_response(content)
    response.headers['Content-Type'] = 'text/plain'
    response.headers['Content-Disposition'] = 'attachment; filename="sample.txt"'
    return response


@app.route('/v1/files/sample.json', methods=['GET'])
def file_sample_json():
    """Returns a JSON file for Downloader testing."""
    return jsonify({
        "message": "Sample JSON file",
        "items": [1, 2, 3],
        "nested": {"key": "value"}
    })


@app.route('/v1/files/binary', methods=['GET'])
def file_binary():
    """Returns a simulated PNG binary file (magic bytes + random data)."""
    import os
    # PNG magic bytes
    png_header = b'\x89PNG\r\n\x1a\n'
    # Add some random data to simulate a real image
    random_data = os.urandom(256)
    content = png_header + random_data
    response = app.make_response(content)
    response.headers['Content-Type'] = 'image/png'
    response.headers['Content-Disposition'] = 'attachment; filename="image.png"'
    return response


@app.route('/v1/files/large', methods=['GET'])
def file_large():
    """Returns a 1MB binary file for large file transfer testing."""
    # Generate 1MB of repeating pattern data
    pattern = b'DownloaderTestData' * 64  # ~1KB block
    content = pattern * 1024  # ~1MB
    content = content[:1048576]  # Exactly 1MB
    response = app.make_response(content)
    response.headers['Content-Type'] = 'application/octet-stream'
    response.headers['Content-Length'] = str(len(content))
    return response


@app.route('/v1/files/redirect', methods=['GET'])
def file_redirect():
    """Returns a 302 redirect to sample.txt for redirect testing."""
    from flask import redirect
    return redirect('/v1/files/sample.txt', code=302)


@app.route('/v1/files/error/<int:status_code>', methods=['GET'])
def file_error(status_code):
    """Returns the specified HTTP error status code for file error testing."""
    messages = {
        400: "Bad Request",
        403: "Forbidden",
        404: "File Not Found",
        500: "Internal Server Error",
        502: "Bad Gateway",
        503: "Service Unavailable"
    }
    message = messages.get(status_code, f"Error {status_code}")
    return jsonify({
        "error": message,
        "status": status_code
    }), status_code


if __name__ == '__main__':
    # Debug mode disabled for security - prevents arbitrary code execution through debugger
    app.run(host='0.0.0.0', port=8080, debug=False)
