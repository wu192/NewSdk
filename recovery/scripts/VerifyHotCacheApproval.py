#!/usr/bin/env python3
"""Fail closed when Full's required-reviewer Environment is unavailable."""
import json
import os
import subprocess


def verify(environment):
    reviewers = [r for r in environment.get('protection_rules', [])
                 if r.get('type') == 'required_reviewers' and r.get('reviewers')]
    if not reviewers:
        raise ValueError('Full disabled: hot-cache-full must have required reviewers; an unprotected/auto-created Environment is insufficient')
    return {'environment': 'hot-cache-full', 'requiredReviewersConfigured': True,
            'approvalBoundary': 'The subsequent Windows job is held by this Environment before any benchmark process starts'}


if __name__ == '__main__':
    if os.environ.get('FULL_EXPLICIT_APPROVAL') != 'true':
        raise SystemExit('Full disabled: the manual full_approved selection is required')
    repo = os.environ['GITHUB_REPOSITORY']
    result = verify(json.loads(subprocess.check_output(
        ['gh', 'api', 'repos/' + repo + '/environments/hot-cache-full'], timeout=30)))
    print(json.dumps(result))
