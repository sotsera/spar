# The evaluation's only way out (decision "M2e's decide-first questions", item 7): an HTTPS proxy that tunnels to the
# model's API and refuses every other host. The agent containers sit on an internal network with this proxy alone.
FROM ubuntu:24.04@sha256:9c7f200c07e3d4a03e7249d2c16bf66a51120fb876aa2242cf31451412543289

RUN apt-get update \
 && apt-get install -y --no-install-recommends tinyproxy \
 && rm -rf /var/lib/apt/lists/*

COPY tinyproxy.conf /etc/tinyproxy/tinyproxy.conf
COPY allowed /etc/tinyproxy/allowed

EXPOSE 8888
CMD ["tinyproxy", "-d", "-c", "/etc/tinyproxy/tinyproxy.conf"]
