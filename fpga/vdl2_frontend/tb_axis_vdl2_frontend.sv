`timescale 1ns/1ps

module tb_axis_vdl2_frontend;
    reg clk = 0;
    reg rstn = 0;
    reg [31:0] s_data = 0;
    reg s_valid = 0;
    wire s_ready;
    wire [31:0] m_data;
    wire m_valid;
    reg m_ready = 1;

    integer in_count = 0;
    integer out_count = 0;
    integer n;

    axis_vdl2_frontend dut (
        .aclk(clk),
        .aresetn(rstn),
        .s_axis_tdata(s_data),
        .s_axis_tvalid(s_valid),
        .s_axis_tready(s_ready),
        .m_axis_tdata(m_data),
        .m_axis_tvalid(m_valid),
        .m_axis_tready(m_ready)
    );

    always #5 clk = ~clk;

    always @(posedge clk) begin
        if (m_valid && m_ready) begin
            out_count <= out_count + 1;
            if (^m_data === 1'bx) begin
                $display("FAIL: X detected on output");
                $fatal(1);
            end
        end
    end

    task send_sample(input signed [15:0] iv, input signed [15:0] qv);
        begin
            @(posedge clk);
            while (!s_ready) @(posedge clk);
            s_data <= {qv, iv};
            s_valid <= 1'b1;
            @(posedge clk);
            while (!s_ready) @(posedge clk);
            s_valid <= 1'b0;
            in_count = in_count + 1;
        end
    endtask

    initial begin
        repeat (4) @(posedge clk);
        rstn <= 1'b1;

        for (n = 0; n < 32; n = n + 1)
            send_sample(n * 100, -(n * 100));

        repeat (10) @(posedge clk);

        $display("input samples=%0d output samples=%0d", in_count, out_count);
        if (out_count != 16) begin
            $display("FAIL: expected exactly 16 decimated outputs");
            $fatal(1);
        end

        $display("PASS");
        $finish;
    end
endmodule
