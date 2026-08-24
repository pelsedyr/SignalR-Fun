#!/bin/bash
# The Service Bus emulator waits on MSSQL and needs ~10s before it accepts AMQP. Without
# this pause the Functions host starts first and buries the startup log under ~1500 lines
# of ConnectionRefused stack traces before its listener retry finally succeeds.
#
# The wait is bounded and never fatal: if the host is unreachable we start anyway and let
# the listener retry, exactly as it did before.
host=${SERVICEBUS_WAIT_HOST:-servicebus-emulator}
port=${SERVICEBUS_WAIT_PORT:-5672}

for _ in $(seq 1 60); do
  (echo > "/dev/tcp/$host/$port") 2>/dev/null && break
  sleep 1
done

exec "$@"
