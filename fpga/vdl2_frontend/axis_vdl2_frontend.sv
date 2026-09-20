// SPDX-License-Identifier: MIT
//
// First-stage VDL2 FPGA front-end for Zynq-7000 + AD936x.
//
// Input/output packing:
//   tdata[15:0]  = signed I
//   tdata[31:16] = signed Q
//
// The first milestone is deliberately small: a symmetric 7-tap FIR
// followed by decimation by 2.  With a 2.1 MS/s input this produces
// 1.05 MS/s, matching dumpvdl2's default 10 samples/symbol processing
// rate for 10.5 ksym/s VDL2.
//
// This block is an integration baseline. Coefficients should be replaced
// by a formally designed filter after RF/test-vector validation.

module axis_vdl2_frontend (
    input  wire         aclk,
    input  wire         aresetn,

    input  wire [31:0]  s_axis_tdata,
    input  wire         s_axis_tvalid,
    output wire         s_axis_tready,

    output reg  [31:0]  m_axis_tdata,
    output reg          m_axis_tvalid,
    input  wire         m_axis_tready
);

    // Approximate low-pass coefficients:
    // [-1, 0, 9, 16, 9, 0, -1] / 32
    // Symmetric, cheap to implement, sufficient for first PL/PS transport tests.
    localparam signed [7:0] C0 = -8'sd1;
    localparam signed [7:0] C1 =  8'sd0;
    localparam signed [7:0] C2 =  8'sd9;
    localparam signed [7:0] C3 =  8'sd16;

    reg signed [15:0] i_d0, i_d1, i_d2, i_d3, i_d4, i_d5, i_d6;
    reg signed [15:0] q_d0, q_d1, q_d2, q_d3, q_d4, q_d5, q_d6;
    reg phase;

    wire out_blocked = m_axis_tvalid && !m_axis_tready;
    assign s_axis_tready = !out_blocked;

    wire signed [16:0] i_s0 = i_d0 + i_d6;
    wire signed [16:0] i_s1 = i_d1 + i_d5;
    wire signed [16:0] i_s2 = i_d2 + i_d4;
    wire signed [16:0] q_s0 = q_d0 + q_d6;
    wire signed [16:0] q_s1 = q_d1 + q_d5;
    wire signed [16:0] q_s2 = q_d2 + q_d4;

    wire signed [25:0] i_acc =
          i_s0 * C0
        + i_s1 * C1
        + i_s2 * C2
        + i_d3 * C3;

    wire signed [25:0] q_acc =
          q_s0 * C0
        + q_s1 * C1
        + q_s2 * C2
        + q_d3 * C3;

    // Coefficients sum to 32, so divide by 32.
    wire signed [20:0] i_scaled = i_acc >>> 5;
    wire signed [20:0] q_scaled = q_acc >>> 5;

    function automatic signed [15:0] sat16(input signed [20:0] x);
        begin
            if (x > 21'sd32767)
                sat16 = 16'sh7fff;
            else if (x < -21'sd32768)
                sat16 = -16'sd32768;
            else
                sat16 = x[15:0];
        end
    endfunction

    always @(posedge aclk) begin
        if (!aresetn) begin
            i_d0 <= 0; i_d1 <= 0; i_d2 <= 0; i_d3 <= 0;
            i_d4 <= 0; i_d5 <= 0; i_d6 <= 0;
            q_d0 <= 0; q_d1 <= 0; q_d2 <= 0; q_d3 <= 0;
            q_d4 <= 0; q_d5 <= 0; q_d6 <= 0;
            phase <= 1'b0;
            m_axis_tdata <= 32'd0;
            m_axis_tvalid <= 1'b0;
        end else begin
            if (m_axis_tvalid && m_axis_tready)
                m_axis_tvalid <= 1'b0;

            if (s_axis_tvalid && s_axis_tready) begin
                i_d6 <= i_d5; i_d5 <= i_d4; i_d4 <= i_d3;
                i_d3 <= i_d2; i_d2 <= i_d1; i_d1 <= i_d0;
                i_d0 <= $signed(s_axis_tdata[15:0]);

                q_d6 <= q_d5; q_d5 <= q_d4; q_d4 <= q_d3;
                q_d3 <= q_d2; q_d2 <= q_d1; q_d1 <= q_d0;
                q_d0 <= $signed(s_axis_tdata[31:16]);

                phase <= ~phase;

                if (phase) begin
                    m_axis_tdata[15:0]  <= sat16(i_scaled);
                    m_axis_tdata[31:16] <= sat16(q_scaled);
                    m_axis_tvalid <= 1'b1;
                end
            end
        end
    end

endmodule
