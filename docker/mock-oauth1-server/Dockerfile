FROM python:3.11-slim

WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Install Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application files
COPY oauth1_validator.py .
COPY app.py .

# Environment variables with defaults
ENV OAUTH1_CONSUMER_KEY=test-consumer-key
ENV OAUTH1_CONSUMER_SECRET=test-consumer-secret
ENV OAUTH1_TOKEN_ID=test-token-id
ENV OAUTH1_TOKEN_SECRET=test-token-secret
ENV OAUTH1_REALM=test-realm
ENV OAUTH1_SIGNATURE_METHOD=HMAC-SHA256,HMAC-SHA1
ENV OAUTH1_TIMESTAMP_TOLERANCE=300
ENV OAUTH1_DEBUG=false
ENV PORT=8081

# Expose port
EXPOSE 8081

# Health check
HEALTHCHECK --interval=10s --timeout=5s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8081/health || exit 1

# Run the application
CMD ["python", "app.py"]
