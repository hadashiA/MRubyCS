# OpCode.Symbol/OpCode.Intern を多数回踏むベンチ。
# シンボルリテラル (:foo) はパース時に PoolValue として保持され、
# 実行時に毎回 Intern される。インラインパック有効なら 0 alloc になる想定。

def loop_syms
  i = 0
  while i < 5000
    a = :abc
    b = :defg
    c = :hi
    d = :jklmn
    e = :ab1
    f = :_op
    i += 1
  end
end

100.times { loop_syms }
