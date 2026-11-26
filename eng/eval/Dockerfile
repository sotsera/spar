# The evaluation's agent image (plan, M2e; decision "M2e's decide-first questions", item 7): Spar's Linux CI image
# (eng/linux-ci/Dockerfile, tagged spar-linux-ci:eval by `run.cs images`) with Claude Code, git for each run's diff, and
# jq for the run's result. Built by eng/eval/run.cs; nothing is published.
ARG BASE=spar-linux-ci:eval
FROM ${BASE}

USER root

RUN apt-get update \
 && apt-get install -y --no-install-recommends git jq \
 && rm -rf /var/lib/apt/lists/*

# Claude Code, pinned: every run of an evaluation uses the same agent.
ARG CLAUDE_CODE_VERSION=2.1.332
RUN npm install --global "@anthropic-ai/claude-code@${CLAUDE_CODE_VERSION}" \
 && claude --version

USER spar
WORKDIR /home/spar

# No update checks, error reports or other traffic beside the model's API, which the proxy is the only way out to.
ENV DISABLE_AUTOUPDATER=1 \
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1 \
    DISABLE_TELEMETRY=1 \
    DISABLE_ERROR_REPORTING=1
