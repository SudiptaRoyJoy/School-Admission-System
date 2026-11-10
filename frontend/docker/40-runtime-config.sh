#!/bin/sh
set -eu
: "${API_BASE_URL:=http://localhost:8080}"
export API_BASE_URL
# Only the public API base URL is injected. Database credentials never enter the browser image.
envsubst '${API_BASE_URL}' \
  < /usr/share/nginx/html/env.template.js \
  > /usr/share/nginx/html/env.js
