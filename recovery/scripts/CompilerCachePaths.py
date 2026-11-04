"""Read-only paths for the NewModCompiler SDK/MOD cache partition."""
import hashlib
import os
from pathlib import Path
import unicodedata


def _utf16_units(value):
    raw = value.encode('utf-16-le', errors='surrogatepass')
    return [chr(int.from_bytes(raw[index:index + 2], 'little'))
            for index in range(0, len(raw), 2)]


def _invariant_upper(value):
    # .NET Framework invariant casing preserves a character when Unicode full
    # uppercasing would expand it (for example sharp-s -> two Latin letters).
    return ''.join(upper if len(upper := char.upper()) == 1 else char for char in value)


def cache_root(sdk, mod):
    """Match CacheLayout's physical path, UTF-16 label and path-only digest."""
    physical_mod = Path(mod).resolve()
    normalized = _invariant_upper(str(physical_mod).replace(os.altsep or os.sep, os.sep).rstrip('\\/')) + os.sep
    digest = hashlib.sha256(normalized.encode('utf-8')).hexdigest()[:32]
    lower_name = ''.join(lower if len(lower := char.lower()) == 1 else char
                         for char in physical_mod.name)
    label = ''.join(char if unicodedata.category(char).startswith('L') or
                    unicodedata.category(char) == 'Nd' or char in '-_' else '_'
                    for char in _utf16_units(lower_name)[:12]) or 'Mod'
    return Path(sdk).resolve() / 'SuperCompilerCache' / (label + '_' + digest)


def cache_layout(sdk, mod):
    root = cache_root(sdk, mod)
    return dict(root=root, **{name: root / name for name in (
        'session', 'build-cache', 'intermediate', 'streams', 'generated', 'temp')})


def session_directory(report, sdk, mod):
    reported = report.get('cache', {}).get('sessionCacheDirectory')
    return Path(reported) if reported else cache_layout(sdk, mod)['session']


def report_cache_root(report, sdk, mod):
    session = session_directory(report, sdk, mod)
    expected = cache_layout(sdk, mod)
    return session.parent if session == expected['session'] else expected['root']
