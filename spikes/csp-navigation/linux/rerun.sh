#!/usr/bin/env bash
# Usage (in the container as uid 1000, from spikes/csp-navigation): linux/rerun.sh <gtk3|gtk4>
# The runs repeated after the first matrix: navigation (redirect step split out) and the cross-origin page.
set -u
v=$1
linux/run.sh $v nav-none none "/nav.html?skip=redirect" --timeout 70
linux/run.sh $v nav-redirect none "/nav.html?skip=assign,replace,unknownScheme,metaRefresh" --timeout 15
linux/run.sh $v nav-base base "/nav.html?skip=redirect" --timeout 70
linux/run.sh $v xpage-base base / --timeout 20 --no-navlock --start-url "http://127.0.0.1:5180/xpage.html?app=spar://app"
linux/run.sh $v xpage-none none / --timeout 20 --no-navlock --start-url "http://127.0.0.1:5180/xpage.html?app=spar://app"
