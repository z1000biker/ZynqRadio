// SPDX-License-Identifier: MIT
//
// Minimal AD936x/libiio IQ source for a dumpvdl2 software baseline.
//
// Writes interleaved signed 16-bit little-endian I,Q samples to stdout.
// Example:
//   ./iio_vdl2_capture ip:192.168.2.1 136975000 2100000 | \\
//     dumpvdl2 --iq-file - --sample-format S16_LE --oversample 20 136.975M
//
// This is intentionally small and is intended for validation before
// moving the sample-rate conversion into FPGA fabric.

#include <iio.h>
#include <errno.h>
#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int wr_attr_ll(struct iio_channel *ch, const char *name, long long v) {
    char buf[64];
    snprintf(buf, sizeof(buf), "%lld", v);
    return (int)iio_channel_attr_write(ch, name, buf);
}

int main(int argc, char **argv) {
    if (argc != 4) {
        fprintf(stderr, "usage: %s <iio-uri> <frequency-hz> <sample-rate>\n", argv[0]);
        return 2;
    }

    const char *uri = argv[1];
    long long freq = strtoll(argv[2], NULL, 10);
    long long rate = strtoll(argv[3], NULL, 10);

    struct iio_context *ctx = iio_create_context_from_uri(uri);
    if (!ctx) {
        fprintf(stderr, "failed to open IIO context %s\n", uri);
        return 1;
    }

    struct iio_device *phy = iio_context_find_device(ctx, "ad9361-phy");
    struct iio_device *rx  = iio_context_find_device(ctx, "cf-ad9361-lpc");
    if (!phy || !rx) {
        fprintf(stderr, "AD936x IIO devices not found\n");
        iio_context_destroy(ctx);
        return 1;
    }

    struct iio_channel *rxlo = iio_device_find_channel(phy, "altvoltage0", true);
    struct iio_channel *rx0  = iio_device_find_channel(phy, "voltage0", false);
    if (!rxlo || !rx0) {
        fprintf(stderr, "AD936x control channels not found\n");
        iio_context_destroy(ctx);
        return 1;
    }

    if (wr_attr_ll(rxlo, "frequency", freq) < 0 ||
        wr_attr_ll(rx0, "sampling_frequency", rate) < 0) {
        fprintf(stderr, "failed to configure frequency/sample rate\n");
        iio_context_destroy(ctx);
        return 1;
    }

    struct iio_channel *i = iio_device_find_channel(rx, "voltage0", false);
    struct iio_channel *q = iio_device_find_channel(rx, "voltage1", false);
    if (!i || !q) {
        fprintf(stderr, "RX streaming channels not found\n");
        iio_context_destroy(ctx);
        return 1;
    }

    iio_channel_enable(i);
    iio_channel_enable(q);

    struct iio_buffer *buf = iio_device_create_buffer(rx, 32768, false);
    if (!buf) {
        fprintf(stderr, "failed to create RX buffer\n");
        iio_context_destroy(ctx);
        return 1;
    }

    fprintf(stderr, "IIO=%s freq=%lld rate=%lld, streaming S16_LE IQ to stdout\n",
            uri, freq, rate);

    while (1) {
        ssize_t n = iio_buffer_refill(buf);
        if (n < 0) {
            fprintf(stderr, "iio_buffer_refill failed: %zd\n", n);
            break;
        }

        ptrdiff_t step = iio_buffer_step(buf);
        char *end = (char *)iio_buffer_end(buf);
        for (char *p = (char *)iio_buffer_first(buf, i); p < end; p += step) {
            // AD936x libiio buffers commonly contain 16-bit containers.
            // Copy I then Q explicitly to guarantee dumpvdl2 S16_LE ordering.
            int16_t iv = *(int16_t *)p;
            int16_t qv = *(int16_t *)(p + sizeof(int16_t));
            if (fwrite(&iv, sizeof(iv), 1, stdout) != 1 ||
                fwrite(&qv, sizeof(qv), 1, stdout) != 1) {
                goto done;
            }
        }
        fflush(stdout);
    }

done:
    iio_buffer_destroy(buf);
    iio_channel_disable(i);
    iio_channel_disable(q);
    iio_context_destroy(ctx);
    return 0;
}
