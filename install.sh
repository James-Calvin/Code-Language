#!/usr/bin/env sh
set -eu

repo="James-Calvin/Code-Language"
os="$(uname -s)"
arch="$(uname -m)"

case "$os" in
  Linux) platform="linux" ;;
  Darwin) platform="osx" ;;
  *)
    echo "Unsupported operating system: $os" >&2
    exit 1
    ;;
esac

case "$arch" in
  x86_64|amd64) cpu="x64" ;;
  arm64|aarch64) cpu="arm64" ;;
  *)
    echo "Unsupported CPU architecture: $arch" >&2
    exit 1
    ;;
esac

asset="code-compiler-$platform-$cpu.zip"
url="https://github.com/$repo/releases/latest/download/$asset"
install_root="$HOME/.code-language"
bin_dir="$install_root/bin"
tmp_dir="${TMPDIR:-/tmp}/code-language-install-$$"
zip_path="$tmp_dir/$asset"

mkdir -p "$bin_dir" "$tmp_dir"

echo "Downloading $url"
if command -v curl >/dev/null 2>&1; then
  curl -fsSL "$url" -o "$zip_path"
elif command -v wget >/dev/null 2>&1; then
  wget -q "$url" -O "$zip_path"
else
  echo "Install requires curl or wget." >&2
  exit 1
fi

if command -v unzip >/dev/null 2>&1; then
  unzip -oq "$zip_path" -d "$tmp_dir/extract"
else
  echo "Install requires unzip." >&2
  exit 1
fi

cp -R "$tmp_dir/extract/." "$bin_dir/"
chmod +x "$bin_dir/compiler" 2>/dev/null || true

case ":$PATH:" in
  *":$bin_dir:"*) echo "$bin_dir is already on PATH." ;;
  *)
    echo "Add this to your shell profile:"
    echo "  export PATH=\"\$HOME/.code-language/bin:\$PATH\""
    ;;
esac

echo "Installed compiler to $bin_dir"
echo "Try: compiler --version"
