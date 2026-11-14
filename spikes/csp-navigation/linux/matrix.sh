#!/usr/bin/env bash
# Usage (in the container as uid 1000, from spikes/csp-navigation): linux/matrix.sh <gtk3|gtk4>
# Every Linux run of the spike for one variant. Device probes are allowed here (?media=1&geo=1): no person can be prompted.
set -u
v=$1
q="?media=1&geo=1"
for p in none base meta ext coep tt report-uri report-to report-to-ext; do linux/run.sh $v probe-$p $p "/probe.html$q" --timeout 30; done
linux/run.sh $v probe-base-media base "/probe.html$q" --timeout 30 --media-stream
linux/run.sh $v probe-base-nosecure base "/probe.html$q" --timeout 30 --no-secure
linux/run.sh $v probe-base-nocors base "/probe.html$q" --timeout 30 --no-cors
linux/run.sh $v probe-coep-nosecure coep "/probe.html$q" --timeout 30 --no-secure
for p in none base meta tt coep; do linux/run.sh $v react-$p $p / --timeout 20; done
SPAR_DIST=dist-inline linux/run.sh $v react-base-inline base / --timeout 20
SPAR_DIST=dist-sri linux/run.sh $v sri-ok sri / --timeout 15
SPAR_DIST=dist-sri linux/run.sh $v sri-tamper-entry sri / --timeout 15 --tamper /assets/index-
SPAR_DIST=dist-sri linux/run.sh $v sri-tamper-lazy sri / --timeout 15 --tamper /assets/Lazy-
SPAR_DIST=dist-sri linux/run.sh $v sri-tamper-css sri / --timeout 15 --tamper .css
SPAR_DIST=dist-sri linux/run.sh $v sri-base-profile base / --timeout 15
linux/run.sh $v nosri-tamper-entry base / --timeout 15 --tamper /assets/index-
linux/run.sh $v nav-none none "/nav.html?skip=redirect" --timeout 70
linux/run.sh $v nav-redirect none "/nav.html?skip=assign,replace,unknownScheme,metaRefresh" --timeout 15
linux/run.sh $v nav-base base "/nav.html?skip=redirect" --timeout 70
linux/run.sh $v xpage-base base / --timeout 20 --no-navlock --start-url "http://127.0.0.1:5180/xpage.html?app=spar://app"
