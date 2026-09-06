#!/usr/bin/env bash
# Roadmap #30: one-line installer - `curl -fsSL https://raw.githubusercontent.com/dopiskur/AgrumyService/master/install.sh | bash`
# (same "curl|bash" convention as Mycodo/scaleTrigger's own installers referenced in the roadmap).
#
# Two top-level paths, chosen independently of the deployment preset below:
#   - Container (Docker or Podman): builds+runs docker-compose.yml (Small) or
#     docker-compose.large.yml (Large/Scaled) from this repo. appsettings.json is never touched -
#     the compose files pass config as environment variables instead, already fully populated
#     before the containers ever start (roadmap #30's explicit container-vs-bare-metal split).
#   - Bare-metal/standalone: downloads the latest release.yml tarballs (self-contained linux-x64,
#     no .NET runtime needed on the target), installs them as systemd services
#     (deploy/agrumy-api.service.template, deploy/agrumy-web.service.template) behind nginx or Apache
#     (deploy/nginx.conf.template / apache.conf.template) with a certbot TLS cert. This path
#     deliberately asks NOTHING about the database - Agrumy.Api boots straight into a minimal
#     setup wizard (Agrumy.Api/Setup/SetupWizard.cs) the first time appsettings.json has no
#     ConnectionStrings:DefaultConnection, and the existing roadmap #91 bootstrap-admin wizard
#     takes over from there once a DB connection is saved.
#
# Deployment preset (independent of the container/bare-metal choice above):
#   - Small/Simple (default): MariaDB, in-process cache (no Redis) - the #14 small-deployment tier.
#   - Large/Scaled: PostgreSQL+TimescaleDB, Redis - the #14 large-deployment tier.
#   - Custom: asks DB provider / TimescaleDB / Redis individually - covers combinations neither
#     preset does (e.g. Postgres+Timescale without Redis).
#
# SAFE TO RE-RUN: every step checks "already done?" before acting - re-running to add a component
# (e.g. Redis) or after an interrupted run does not repeat completed steps or overwrite existing
# secrets.

set -euo pipefail

REPO="dopiskur/AgrumyService"
RAW_BASE="https://raw.githubusercontent.com/${REPO}/master"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-.}")" && pwd)"
# Roadmap #30: works whether install.sh is run from a cloned checkout (SCRIPT_DIR has
# docker-compose.yml/deploy/ right next to it) or piped through `curl | bash` (no local repo at
# all - IN_REPO is empty, every asset this script needs gets fetched from RAW_BASE instead).
IN_REPO=""
[ -f "${SCRIPT_DIR}/docker-compose.yml" ] && IN_REPO="1"

log()  { printf '\n==> %s\n' "$*"; }
warn() { printf 'WARNING: %s\n' "$*" >&2; }
err()  { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
has_cmd() { command -v "$1" >/dev/null 2>&1; }

# `sudo` only when not already root (a from-scratch container/CI run is often already root, where
# `sudo` itself may not even be installed yet).
as_root() { if [ "$(id -u)" = "0" ]; then "$@"; else sudo "$@"; fi; }

random_secret() {
  # 48 bytes -> ~64 base64 chars, well over JWT:SecureKey's 32-char minimum; strip characters that
  # would need escaping when this lands inside a shell-quoted .env value or a JSON string.
  openssl rand -base64 48 | tr -d '\n=+/'
}

fetch() {
  # $1 = path relative to repo root, $2 = destination file.
  if [ -n "$IN_REPO" ]; then
    cp "${SCRIPT_DIR}/$1" "$2"
  else
    curl -fsSL "${RAW_BASE}/$1" -o "$2"
  fi
}

detect_pkg_manager() {
  if has_cmd apt-get; then echo apt
  elif has_cmd dnf; then echo dnf
  elif has_cmd yum; then echo yum
  else echo unknown
  fi
}

install_pkg() {
  local pkg="$1"
  case "$(detect_pkg_manager)" in
    apt) as_root apt-get update -qq && as_root apt-get install -y "$pkg" ;;
    dnf) as_root dnf install -y "$pkg" ;;
    yum) as_root yum install -y "$pkg" ;;
    *) err "No supported package manager (apt/dnf/yum) found - install '$pkg' manually and re-run install.sh." ;;
  esac
}

ensure_cmd() {
  # $1 = command to check, $2 = package name if it's missing (usually the same).
  local cmd="$1" pkg="${2:-$1}"
  if ! has_cmd "$cmd"; then
    log "Installing prerequisite: $pkg"
    install_pkg "$pkg"
  fi
}

# ============================================================================================
# 1. Top-level menu
# ============================================================================================

echo "Agrumy installer"
echo
echo "1) Quick install"
echo "2) Custom install (choose every option)"
read -rp "Choice [1]: " TOP_CHOICE
TOP_CHOICE="${TOP_CHOICE:-1}"

PRESET=""       # small | large | custom
DB_PROVIDER=""  # mysql | postgres
USE_TIMESCALE=""
USE_REDIS=""

if [ "$TOP_CHOICE" = "1" ]; then
  echo
  echo "1.1) Simple/Small deployment (MariaDB, no Redis) [default]"
  echo "1.2) Large/Scaled deployment (PostgreSQL + TimescaleDB, Redis)"
  read -rp "Choice [1.1]: " QUICK_CHOICE
  QUICK_CHOICE="${QUICK_CHOICE:-1.1}"
  if [ "$QUICK_CHOICE" = "1.2" ]; then
    PRESET="large"; DB_PROVIDER="postgres"; USE_TIMESCALE="yes"; USE_REDIS="yes"
  else
    PRESET="small"; DB_PROVIDER="mysql"; USE_TIMESCALE="no"; USE_REDIS="no"
  fi
else
  PRESET="custom"
  read -rp "Database provider - (m)ysql/mariadb or (p)ostgres? [m]: " DB_CHOICE
  DB_CHOICE="${DB_CHOICE:-m}"
  if [ "$DB_CHOICE" = "p" ]; then
    DB_PROVIDER="postgres"
    read -rp "Enable TimescaleDB for sensor data (recommended for large fleets)? [y/N]: " TS_CHOICE
    USE_TIMESCALE="no"; [ "${TS_CHOICE:-n}" = "y" ] && USE_TIMESCALE="yes"
  else
    DB_PROVIDER="mysql"
    USE_TIMESCALE="no"   # roadmap #14: TimescaleDB is Postgres-only, not offered for MySQL/MariaDB
  fi
  read -rp "Enable Redis distributed cache (roadmap #72; needed to scale Agrumy.Api horizontally)? [y/N]: " REDIS_CHOICE
  USE_REDIS="no"; [ "${REDIS_CHOICE:-n}" = "y" ] && USE_REDIS="yes"
fi

echo
read -rp "Deployment mode - (c)ontainer [Docker/Podman] or (b)are-metal/standalone? [c]: " MODE_CHOICE
MODE_CHOICE="${MODE_CHOICE:-c}"

# ============================================================================================
# 2. Container path (Docker or Podman)
# ============================================================================================

install_container() {
  local compose_cmd=""
  if has_cmd docker && docker compose version >/dev/null 2>&1; then
    compose_cmd="docker compose"
  elif has_cmd podman-compose; then
    compose_cmd="podman-compose"
  else
    log "No Docker or Podman found - which do you want to install?"
    read -rp "(d)ocker or (p)odman? [d]: " ENGINE_CHOICE
    ENGINE_CHOICE="${ENGINE_CHOICE:-d}"
    if [ "$ENGINE_CHOICE" = "p" ]; then
      install_pkg podman
      ensure_cmd podman-compose podman-compose
      compose_cmd="podman-compose"
    else
      # Roadmap #30: the distro package (not Docker's own convenience script, which the roadmap's
      # "detects and installs through the OS package manager" phrasing implies staying inside)
      # already includes the compose plugin on recent Debian/Ubuntu/Fedora.
      install_pkg docker.io || install_pkg docker-ce
      as_root systemctl enable --now docker
      compose_cmd="docker compose"
    fi
  fi

  # The Dockerfiles COPY source (Agrumy.Shared/Agrumy.Dal/Agrumy.Api or Agrumy.Web) at build time -
  # the compose file alone is not enough build context. Already inside a checkout (IN_REPO)? Build
  # right there. Otherwise (`curl | bash`, no local checkout at all) clone the repo into a fresh
  # ./agrumy directory and build from that instead - either way, no redundant one-file-at-a-time
  # fetching, the compose/Dockerfile/docker-compose.large.yml content all comes along for free.
  local work_dir
  if [ -n "$IN_REPO" ]; then
    work_dir="$SCRIPT_DIR"
  else
    work_dir="$(pwd)/agrumy"
    if [ ! -d "${work_dir}/.git" ]; then
      ensure_cmd git git
      log "Cloning ${REPO} (build context for the Dockerfiles)"
      git clone --depth 1 "https://github.com/${REPO}.git" "$work_dir"
    fi
  fi
  cd "$work_dir"

  local compose_file="docker-compose.yml"
  [ "$PRESET" = "large" ] && compose_file="docker-compose.large.yml"
  [ "$PRESET" = "custom" ] && [ "$DB_PROVIDER" = "postgres" ] && compose_file="docker-compose.large.yml"

  if [ ! -f ".env" ]; then
    log "Generating .env (DB_PASSWORD / JWT_SECRET)"
    {
      echo "DB_PASSWORD=$(random_secret)"
      echo "JWT_SECRET=$(random_secret)"
      read -rp "Public domain this install will answer on (e.g. agrumy.example.com) [https://api.agrumy.local]: " ISSUER
      echo "AGRUMY_JWT_ISSUER=${ISSUER:-https://api.agrumy.local}"
    } > .env
    chmod 600 .env
  else
    log ".env already exists - keeping it (re-run reuses the same secrets)."
  fi

  local profile_args=()
  [ "$PRESET" = "large" ] && [ "$USE_REDIS" = "yes" ] && profile_args=(--profile redis)
  [ "$PRESET" = "custom" ] && [ "$USE_REDIS" = "yes" ] && [ "$DB_PROVIDER" = "postgres" ] && profile_args=(--profile redis)

  log "Building and starting containers ($compose_file)"
  $compose_cmd -f "$compose_file" "${profile_args[@]}" up -d --build

  echo
  echo "Agrumy is starting. Once containers report healthy:"
  echo "  Agrumy.Api  -> http://localhost:5000"
  echo "  Agrumy.Web  -> http://localhost:5001 (open this first to set the Global Admin password)"
  echo "Put your own reverse proxy (with TLS) in front of these ports for a public domain."
}

# ============================================================================================
# 3. Bare-metal / standalone path
# ============================================================================================

latest_release_tag() {
  curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" \
    | grep -m1 '"tag_name"' | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/'
}

download_and_install_app() {
  # $1 = Agrumy.Api | Agrumy.Web, $2 = version tag (e.g. v1.0.0), $3 = install dir, $4 = service user
  local app="$1" tag="$2" install_dir="$3" service_user="$4"
  local app_lower tarball_name tarball_url tmp_tar tmp_sums expected_sha actual_sha
  app_lower="$(echo "$app" | tr '[:upper:]' '[:lower:]' | tr -d '.')"
  tarball_name="agrumy-${app_lower}-${tag}.tar.gz"
  tarball_url="https://github.com/${REPO}/releases/download/${tag}/${tarball_name}"

  as_root mkdir -p "$install_dir"
  tmp_tar="$(mktemp)"
  log "Downloading $app $tag"
  curl -fsSL "$tarball_url" -o "$tmp_tar" || err "Could not download $tarball_url - has a release been tagged yet? (git tag vX.Y.Z && git push origin vX.Y.Z on ${REPO})"

  # release.yml publishes SHA256SUMS.txt next to every tarball - verify before this script (running
  # as root) extracts and executes anything downloaded over the wire.
  tmp_sums="$(mktemp)"
  curl -fsSL "https://github.com/${REPO}/releases/download/${tag}/SHA256SUMS.txt" -o "$tmp_sums" \
    || err "Could not download SHA256SUMS.txt for ${tag} - refusing to install an unverified binary."
  expected_sha="$(grep -F -- "$tarball_name" "$tmp_sums" | awk '{print $1}')"
  [ -n "$expected_sha" ] || err "SHA256SUMS.txt has no entry for ${tarball_name}."
  actual_sha="$(sha256sum "$tmp_tar" | awk '{print $1}')"
  [ "$actual_sha" = "$expected_sha" ] || err "Checksum mismatch for ${tarball_name}: expected ${expected_sha}, got ${actual_sha}. Aborting install."
  rm -f "$tmp_sums"

  as_root tar -xzf "$tmp_tar" -C "$install_dir"
  rm -f "$tmp_tar"
  # pscp/scp losing the execute bit is a known trap (CLAUDE.md) - tar preserves it, but set it
  # explicitly anyway so a re-run after a manual file replacement can't silently regress this.
  as_root chmod +x "${install_dir}/${app}"
  as_root chown -R "${service_user}:${service_user}" "$install_dir"
}

write_appsettings_api() {
  # $1 = install dir, $2 = jwt secret, $3 = issuer domain, $4 = service user
  local dir="$1" jwt="$2" issuer="$3" service_user="$4"
  if [ -f "${dir}/appsettings.json" ]; then
    log "appsettings.json already exists for Agrumy.Api - keeping it (re-run does not overwrite secrets)."
    return
  fi
  # Roadmap #30: deliberately NO ConnectionStrings/Database section - SetupWizard fills that in
  # on first boot. Urls is required (CLAUDE.md: neither .service file sets ASPNETCORE_URLS).
  as_root tee "${dir}/appsettings.json" > /dev/null <<JSON
{
  "Urls": "http://localhost:5000",
  "JWT": { "SecureKey": "${jwt}", "Issuer": "${issuer}", "Audience": "agrumy-api" },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
JSON
  # `tee` above wrote the file as root/sudo - fix ownership so it matches the rest of the install
  # dir (download_and_install_app's chown -R already ran before this call), same reasoning: the
  # systemd unit runs as SERVICE_USER, not root, and must be able to read (and later, the setup
  # wizard, WRITE) this file.
  as_root chown "${service_user}:${service_user}" "${dir}/appsettings.json"
}

write_appsettings_web() {
  # $1 = install dir, $2 = jwt secret, $3 = issuer domain, $4 = api base url, $5 = dataprotection key path, $6 = service user
  local dir="$1" jwt="$2" issuer="$3" api_url="$4" key_path="$5" service_user="$6"
  if [ -f "${dir}/appsettings.json" ]; then
    log "appsettings.json already exists for Agrumy.Web - keeping it (re-run does not overwrite secrets)."
    return
  fi
  as_root tee "${dir}/appsettings.json" > /dev/null <<JSON
{
  "Urls": "http://localhost:5001",
  "WebView": { "ApiService": "${api_url}" },
  "JWT": { "SecureKey": "${jwt}", "Issuer": "${issuer}", "Audience": "agrumy-api" },
  "DataProtection": { "KeyPath": "${key_path}" },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
JSON
  as_root chown "${service_user}:${service_user}" "${dir}/appsettings.json"
}

install_systemd_unit() {
  # $1 = template name (e.g. agrumy-api.service.template), $2 = install dir, $3 = service user
  local template="$1" install_dir="$2" service_user="$3"
  local unit_name="${template%.template}"
  local tmp
  tmp="$(mktemp)"
  fetch "deploy/${template}" "$tmp"
  sed -e "s|{{INSTALL_DIR}}|${install_dir}|g" -e "s|{{SERVICE_USER}}|${service_user}|g" "$tmp" \
    | as_root tee "/etc/systemd/system/${unit_name}" > /dev/null
  rm -f "$tmp"
}

install_reverse_proxy_hostname() {
  # $1 = nginx|apache, $2 = api domain, $3 = admin domain
  local kind="$1" api_domain="$2" admin_domain="$3" tmp
  tmp="$(mktemp)"
  if [ "$kind" = "apache" ]; then
    ensure_cmd apache2ctl apache2
    as_root a2enmod proxy proxy_http headers
    fetch "deploy/apache.conf.template" "$tmp"
    sed -e "s|{{API_DOMAIN}}|${api_domain}|g" -e "s|{{ADMIN_DOMAIN}}|${admin_domain}|g" \
        -e "s|{{API_PORT}}|5000|g" -e "s|{{WEB_PORT}}|5001|g" "$tmp" \
      | as_root tee /etc/apache2/sites-available/agrumy.conf > /dev/null
    as_root a2ensite agrumy
    as_root systemctl reload apache2
    ensure_cmd certbot certbot
    install_pkg python3-certbot-apache
    as_root certbot --apache -d "$api_domain" -d "$admin_domain" --non-interactive --agree-tos -m "admin@${api_domain}" || warn "certbot did not complete - re-run 'sudo certbot --apache' by hand once DNS for $api_domain/$admin_domain resolves to this server."
  else
    ensure_cmd nginx nginx
    fetch "deploy/nginx.conf.template" "$tmp"
    sed -e "s|{{API_DOMAIN}}|${api_domain}|g" -e "s|{{ADMIN_DOMAIN}}|${admin_domain}|g" \
        -e "s|{{API_PORT}}|5000|g" -e "s|{{WEB_PORT}}|5001|g" "$tmp" \
      | as_root tee /etc/nginx/sites-available/agrumy.conf > /dev/null
    as_root ln -sf /etc/nginx/sites-available/agrumy.conf /etc/nginx/sites-enabled/agrumy.conf
    as_root nginx -t && as_root systemctl reload nginx
    ensure_cmd certbot certbot
    install_pkg python3-certbot-nginx
    as_root certbot --nginx -d "$api_domain" -d "$admin_domain" --non-interactive --agree-tos -m "admin@${api_domain}" || warn "certbot did not complete - re-run 'sudo certbot --nginx' by hand once DNS for $api_domain/$admin_domain resolves to this server."
  fi
  rm -f "$tmp"
}

# A bare IP has no real hostname for Let's Encrypt to validate - self-signed is the fallback, not plain HTTP, so local traffic still gets encrypted.
generate_self_signed_cert() {
  # $1 = IP, $2 = cert path, $3 = key path
  local ip="$1" cert_path="$2" key_path="$3"
  [ -f "$cert_path" ] && return
  ensure_cmd openssl openssl
  as_root mkdir -p "$(dirname "$cert_path")"
  log "Generating a self-signed TLS cert for ${ip} (Let's Encrypt cannot issue one for a bare IP)"
  as_root openssl req -x509 -newkey rsa:4096 -nodes -days 3650 \
    -keyout "$key_path" -out "$cert_path" \
    -subj "/CN=${ip}" -addext "subjectAltName=IP:${ip}"
  as_root chmod 600 "$key_path"
}

# Single domain (or bare IP), /api split by path instead of by hostname.
install_reverse_proxy_path() {
  # $1 = nginx|apache, $2 = domain or IP, $3 = yes|selfsigned (yes = certbot for a real domain, selfsigned = bare IP)
  local kind="$1" domain="$2" tls_mode="$3" tmp cert_path key_path
  tmp="$(mktemp)"
  if [ "$tls_mode" = "selfsigned" ]; then
    cert_path="/etc/agrumy/tls/agrumy.crt"; key_path="/etc/agrumy/tls/agrumy.key"
    generate_self_signed_cert "$domain" "$cert_path" "$key_path"
  fi

  if [ "$kind" = "apache" ]; then
    ensure_cmd apache2ctl apache2
    if [ "$tls_mode" = "selfsigned" ]; then
      as_root a2enmod proxy proxy_http headers ssl
      fetch "deploy/apache-path-selfsigned.conf.template" "$tmp"
      sed -e "s|{{DOMAIN}}|${domain}|g" -e "s|{{API_PORT}}|5000|g" -e "s|{{WEB_PORT}}|5001|g" \
          -e "s|{{CERT_PATH}}|${cert_path}|g" -e "s|{{KEY_PATH}}|${key_path}|g" "$tmp" \
        | as_root tee /etc/apache2/sites-available/agrumy.conf > /dev/null
      as_root a2ensite agrumy
      as_root systemctl reload apache2
    else
      as_root a2enmod proxy proxy_http headers
      fetch "deploy/apache-path.conf.template" "$tmp"
      sed -e "s|{{DOMAIN}}|${domain}|g" -e "s|{{API_PORT}}|5000|g" -e "s|{{WEB_PORT}}|5001|g" "$tmp" \
        | as_root tee /etc/apache2/sites-available/agrumy.conf > /dev/null
      as_root a2ensite agrumy
      as_root systemctl reload apache2
      ensure_cmd certbot certbot
      install_pkg python3-certbot-apache
      as_root certbot --apache -d "$domain" --non-interactive --agree-tos -m "admin@${domain}" || warn "certbot did not complete - re-run 'sudo certbot --apache' by hand once DNS for $domain resolves to this server."
    fi
  else
    ensure_cmd nginx nginx
    if [ "$tls_mode" = "selfsigned" ]; then
      fetch "deploy/nginx-path-selfsigned.conf.template" "$tmp"
      sed -e "s|{{DOMAIN}}|${domain}|g" -e "s|{{API_PORT}}|5000|g" -e "s|{{WEB_PORT}}|5001|g" \
          -e "s|{{CERT_PATH}}|${cert_path}|g" -e "s|{{KEY_PATH}}|${key_path}|g" "$tmp" \
        | as_root tee /etc/nginx/sites-available/agrumy.conf > /dev/null
    else
      fetch "deploy/nginx-path.conf.template" "$tmp"
      sed -e "s|{{DOMAIN}}|${domain}|g" -e "s|{{API_PORT}}|5000|g" -e "s|{{WEB_PORT}}|5001|g" "$tmp" \
        | as_root tee /etc/nginx/sites-available/agrumy.conf > /dev/null
    fi
    as_root ln -sf /etc/nginx/sites-available/agrumy.conf /etc/nginx/sites-enabled/agrumy.conf
    as_root nginx -t && as_root systemctl reload nginx
    if [ "$tls_mode" = "yes" ]; then
      ensure_cmd certbot certbot
      install_pkg python3-certbot-nginx
      as_root certbot --nginx -d "$domain" --non-interactive --agree-tos -m "admin@${domain}" || warn "certbot did not complete - re-run 'sudo certbot --nginx' by hand once DNS for $domain resolves to this server."
    fi
  fi
  rm -f "$tmp"
}

# Small preset promises "everything on one box" - the local MariaDB server that implies is the one thing bare-metal+Small never actually installed.
install_local_mariadb() {
  local creds_file="/opt/agrumy/db-credentials.txt"
  if has_cmd mysql || has_cmd mariadb; then
    log "MariaDB client already present - assuming the server is installed."
  else
    log "Installing MariaDB server (Small preset runs its own DB on this box)"
    install_pkg mariadb-server
  fi
  as_root systemctl enable --now mariadb 2>/dev/null || as_root systemctl enable --now mysql

  if [ -f "$creds_file" ]; then
    log "Local Agrumy database already provisioned - reusing existing credentials (${creds_file})."
    return
  fi

  local db_password
  db_password="$(random_secret)"
  as_root mysql -e "CREATE DATABASE IF NOT EXISTS agrumy;"
  as_root mysql -e "CREATE USER IF NOT EXISTS 'agrumy'@'localhost' IDENTIFIED BY '${db_password}'; GRANT ALL PRIVILEGES ON agrumy.* TO 'agrumy'@'localhost'; FLUSH PRIVILEGES;"

  as_root mkdir -p /opt/agrumy
  as_root tee "$creds_file" > /dev/null <<CREDS
Host: localhost
Port: 3306
Database: agrumy
Username: agrumy
Password: ${db_password}
CREDS
  as_root chmod 600 "$creds_file"

  log "Local MariaDB ready - paste these into the setup wizard (also saved to ${creds_file}):"
  echo "  Host: localhost"
  echo "  Port: 3306"
  echo "  Database: agrumy"
  echo "  Username: agrumy"
  echo "  Password: ${db_password}"
}

install_baremetal() {
  echo
  echo "1) Hostname-based (two domains, e.g. api.example.com + admin.example.com)"
  echo "2) Path-based (one domain or IP - /api goes to Agrumy.Api, everything else to Agrumy.Web)"
  read -rp "Routing [1]: " ROUTING_CHOICE
  ROUTING_CHOICE="${ROUTING_CHOICE:-1}"

  local scheme="https" api_url="" issuer="" use_tls="yes"
  if [ "$ROUTING_CHOICE" = "2" ]; then
    read -rp "Domain or IP this install will answer on (e.g. agrumy.example.com or its bare IP): " ROUTING_DOMAIN
    [ -z "$ROUTING_DOMAIN" ] && err "A domain or IP is required."
    # Let's Encrypt cannot issue a cert for a bare IP - self-signed still encrypts local traffic, unlike a plain-HTTP fallback.
    if [[ "$ROUTING_DOMAIN" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]]; then
      use_tls="selfsigned"
      warn "Routing by IP - Let's Encrypt can't issue a cert for it, using a self-signed one instead (your browser will warn once - safe to accept for your own box)."
    fi
    issuer="${scheme}://${ROUTING_DOMAIN}"
    api_url="$issuer"
  else
    read -rp "API domain (e.g. api.example.com): " API_DOMAIN
    [ -z "$API_DOMAIN" ] && err "API domain is required."
    read -rp "Admin UI domain (e.g. admin.example.com): " ADMIN_DOMAIN
    [ -z "$ADMIN_DOMAIN" ] && err "Admin UI domain is required."
    issuer="https://${API_DOMAIN}"
    api_url="https://${API_DOMAIN}"
  fi

  if [ "$PRESET" = "custom" ]; then
    read -rp "Reverse proxy - (n)ginx or (a)pache? [n]: " PROXY_CHOICE
    PROXY_CHOICE="${PROXY_CHOICE:-n}"
  elif [ "$PRESET" = "small" ]; then
    PROXY_CHOICE="a"
    log "Quick install (Small) - using Apache as the reverse proxy."
  else
    PROXY_CHOICE="n"
    log "Quick install (Large) - using nginx as the reverse proxy."
  fi

  read -rp "Service account to run Agrumy as [www-data]: " SERVICE_USER
  SERVICE_USER="${SERVICE_USER:-www-data}"
  if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    as_root useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
  fi

  [ "$PRESET" = "small" ] && install_local_mariadb

  local api_dir="/opt/agrumy/api" web_dir="/opt/agrumy/web" keys_dir="/opt/agrumy/dataprotection-keys"
  as_root mkdir -p "$keys_dir" && as_root chown "${SERVICE_USER}:${SERVICE_USER}" "$keys_dir"

  local tag
  tag="$(latest_release_tag)"
  [ -z "$tag" ] && err "Could not find a published release for ${REPO}. Tag one first: git tag v1.0.0 && git push origin v1.0.0 (see .github/workflows/release.yml)."
  log "Installing release ${tag}"

  download_and_install_app "Agrumy.Api" "$tag" "$api_dir" "$SERVICE_USER"
  download_and_install_app "Agrumy.Web" "$tag" "$web_dir" "$SERVICE_USER"

  local jwt_secret
  jwt_secret="$(random_secret)"
  write_appsettings_api "$api_dir" "$jwt_secret" "$issuer" "$SERVICE_USER"
  write_appsettings_web "$web_dir" "$jwt_secret" "$issuer" "$api_url" "$keys_dir" "$SERVICE_USER"

  install_systemd_unit "agrumy-api.service.template" "$api_dir" "$SERVICE_USER"
  install_systemd_unit "agrumy-web.service.template" "$web_dir" "$SERVICE_USER"
  as_root systemctl daemon-reload
  as_root systemctl enable --now agrumy-api.service
  as_root systemctl enable --now agrumy-web.service

  local proxy_kind="nginx"
  [ "$PROXY_CHOICE" = "a" ] && proxy_kind="apache"

  echo
  if [ "$ROUTING_CHOICE" = "2" ]; then
    install_reverse_proxy_path "$proxy_kind" "$ROUTING_DOMAIN" "$use_tls"
    echo "Agrumy.Api and Agrumy.Web are installed and running."
    echo "Next: open ${scheme}://${ROUTING_DOMAIN}/api to finish the database setup wizard (see the service log for the ?token=... value)."
    echo "Then: open ${scheme}://${ROUTING_DOMAIN}/ to set the Global Admin password (roadmap #91)."
  else
    install_reverse_proxy_hostname "$proxy_kind" "$API_DOMAIN" "$ADMIN_DOMAIN"
    echo "Agrumy.Api and Agrumy.Web are installed and running."
    echo "Next: open https://${API_DOMAIN}/ to finish the database setup wizard."
    echo "Then: open https://${ADMIN_DOMAIN}/ to set the Global Admin password (roadmap #91)."
  fi
}

# ============================================================================================
# 4. Dispatch
# ============================================================================================

if [ "$MODE_CHOICE" = "b" ]; then
  install_baremetal
else
  install_container
fi
