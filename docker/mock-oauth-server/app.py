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

def validate_basic_auth():
    """Validate HTTP Basic Authentication"""
    auth_header = request.headers.get('Authorization')
    if not auth_header or not auth_header.startswith('Basic '):
        return None, None

    try:
        credentials = base64.b64decode(auth_header[6:]).decode('utf-8')
        client_id, client_secret = credentials.split(':', 1)

        if client_id in CLIENTS and CLIENTS[client_id] == client_secret:
            return client_id, client_secret
        return None, None
    except:
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

    # Validate client credentials via Basic Auth
    client_id, client_secret = validate_basic_auth()

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

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=8080, debug=True)
