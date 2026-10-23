#!/usr/bin/env python3
"""Offline experiment; this does not change Radarr's matching or import behavior."""

import json
import sys


def needs_review(expected, actual, *, opted_in=False, automatic=True,
                 single_file=True, alternate_cut=False):
    if not (opted_in and automatic and single_file) or alternate_cut:
        return False
    if expected is None or actual is None or expected <= 0 or actual <= 0:
        return False
    # Experimental policy: grossly short runtime warrants review, not another identity.
    return actual < expected * 0.60


if __name__ == "__main__":
    assert needs_review(173, 5163.456 / 60, opted_in=True)
    for expected, actual in [(173, 173), (173, 166), (173, 190), (86, 86.06),
                             (92, 86.06), (173, 103.8), (None, 86), (173, None),
                             (0, 86), (173, 0)]:
        assert not needs_review(expected, actual, opted_in=True)
    assert not needs_review(173, 86)
    for exemption in [{"automatic": False}, {"single_file": False}, {"alternate_cut": True}]:
        assert not needs_review(173, 86, opted_in=True, **exemption)
    print("15 experimental policy checks passed; no production integration.")
    if len(sys.argv) > 1:
        with open(sys.argv[1]) as source:
            rows = json.load(source)
        flagged = sum(needs_review(r["expected"], r["actual"], opted_in=True) for r in rows)
        print(f"Stored-duration baseline: {flagged}/{len(rows)} flagged when all are opted in.")
