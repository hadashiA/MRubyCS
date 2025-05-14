# NOTE: Methods containing loop must be implemented in .rb(bytecode) to support jumps by break.

class BasicObject
  def !=(other)
    if self == other
      false
    else
      true
    end
  end
end

module Kernel
  ##
  # call-seq:
  #    obj.extend(module, ...)    -> obj
  #
  # Adds to _obj_ the instance methods from each module given as a
  # parameter.
  #
  #    module Mod
  #      def hello
  #        "Hello from Mod.\n"
  #      end
  #    end
  #
  #    class Klass
  #      def hello
  #        "Hello from Klass.\n"
  #      end
  #    end
  #
  #    k = Klass.new
  #    k.hello         #=> "Hello from Klass.\n"
  #    k.extend(Mod)   #=> #<Klass:0x401b3bc8>
  #    k.hello         #=> "Hello from Mod.\n"
  #
  # ISO 15.3.1.3.13
  def extend(*args)
    args.reverse!
    obj = self
    args.each do |m|
      m.__send__(:extend_object, obj)
      m.__send__(:extended, obj)
    end
    self
  end
end

##
# Kernel
#
# ISO 15.3.1
module Kernel

  # 15.3.1.2.1 Kernel.`
  # provided by Kernel#`
  # 15.3.1.3.3
  def `(s)
    raise NotImplementedError.new("backquotes not implemented")
  end

  ##
  # 15.3.1.2.3  Kernel.eval
  # 15.3.1.3.12 Kernel#eval
  # NotImplemented by mruby core; use mruby-eval gem

  ##
  # ISO 15.3.1.2.8 Kernel.loop
  # not provided by mruby

  ##
  # Calls the given block repetitively.
  #
  # ISO 15.3.1.3.29
  private def loop(&block)
    return to_enum :loop unless block

    while true
      yield
    end
  rescue StopIteration => e
    e.result
  end

  # 11.4.4 Step c)
  def !~(y)
    !(self =~ y)
  end

  def to_enum(*a)
    raise NotImplementedError.new("fiber required for enumerator")
  end
end

# ISO 15.2.31
class NameError < StandardError
  attr_accessor :name

  def initialize(message=nil, name=nil)
    @name = name
    super(message)
  end
end

# ISO 15.2.32
class NoMethodError < NameError
  attr_reader :args

  def initialize(message=nil, name=nil, args=nil)
    @args = args
    super message, name
  end
end

class Module
  # 15.2.2.4.11
  alias attr attr_reader

  # 15.2.2.4.27
  def include(*args)
    args.reverse!
    mod = self
    args.each do |m|
      m.__send__(:append_features, mod)
      m.__send__(:included, mod)
    end
    self
  end

  def prepend(*args)
    args.reverse!
    mod = self
    args.each do |m|
      m.__send__(:prepend_features, mod)
      m.__send__(:prepended, mod)
    end
    self
  end
end

##
# Enumerable
#
# The <code>Enumerable</code> mixin provides collection classes with
# several traversal and searching methods, and with the ability to
# sort. The class must provide a method `each`, which
# yields successive members of the collection. If
# {Enumerable#max}, {#min}, or
# {#sort} is used, the objects in the collection must also
# implement a meaningful `<=>` operator, as these methods
# rely on an ordering between members of the collection.
#
# ISO 15.3.2
module Enumerable

  NONE = Object.new

  ##
  # Call the given block for each element
  # which is yield by +each+. Return false
  # if one block value is false. Otherwise
  # return true. If no block is given and
  # +self+ is false return false.
  #
  # ISO 15.3.2.2.1
  def all?(&block)
    if block
      self.each{|*val| return false unless block.call(*val)}
    else
      self.each{|*val| return false unless val.__svalue}
    end
    true
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Return true
  # if one block value is true. Otherwise
  # return false. If no block is given and
  # +self+ is true object return true.
  #
  # ISO 15.3.2.2.2
  def any?(&block)
    if block
      self.each{|*val| return true if block.call(*val)}
    else
      self.each{|*val| return true if val.__svalue}
    end
    false
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Append all
  # values of each block together and
  # return this value.
  #
  # ISO 15.3.2.2.3
  def collect(&block)
    return to_enum :collect unless block

    ary = []
    self.each{|*val| ary.push(block.call(*val))}
    ary
  end

  ##
  # Return the first element for which
  # value from the block is true. If no
  # object matches, calls +ifnone+ and
  # returns its result. Otherwise returns
  # +nil+.
  #
  # ISO 15.3.2.2.4
  def detect(ifnone=nil, &block)
    return to_enum :detect, ifnone unless block

    self.each{|*val|
      if block.call(*val)
        return val.__svalue
      end
    }
    ifnone.call unless ifnone.nil?
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Pass an
  # index to the block which starts at 0
  # and increase by 1 for each element.
  #
  # ISO 15.3.2.2.5
  def each_with_index(&block)
    return to_enum :each_with_index unless block

    i = 0
    self.each{|*val|
      block.call(val.__svalue, i)
      i += 1
    }
    self
  end

  ##
  # Return an array of all elements which
  # are yield by +each+.
  #
  # ISO 15.3.2.2.6
  def entries
    ary = []
    self.each{|*val|
      # __svalue is an internal method
      ary.push val.__svalue
    }
    ary
  end

  ##
  # Alias for find
  #
  # ISO 15.3.2.2.7
  alias find detect

  ##
  # Call the given block for each element
  # which is yield by +each+. Return an array
  # which contains all elements whose block
  # value was true.
  #
  # ISO 15.3.2.2.8
  def find_all(&block)
    return to_enum :find_all unless block

    ary = []
    self.each{|*val|
      ary.push(val.__svalue) if block.call(*val)
    }
    ary
  end

  ##
  # Call the given block for each element
  # which is yield by +each+ and which return
  # value was true when invoking === with
  # +pattern+. Return an array with all
  # elements or the respective block values.
  #
  # ISO 15.3.2.2.9
  def grep(pattern, &block)
    ary = []
    self.each{|*val|
      sv = val.__svalue
      if pattern === sv
        ary.push((block)? block.call(*val): sv)
      end
    }
    ary
  end

  ##
  # Return true if at least one element which
  # is yield by +each+ returns a true value
  # by invoking == with +obj+. Otherwise return
  # false.
  #
  # ISO 15.3.2.2.10
  def include?(obj)
    self.each{|*val|
      return true if val.__svalue == obj
    }
    false
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Return value
  # is the sum of all block values. Pass
  # to each block the current sum and the
  # current element.
  #
  # ISO 15.3.2.2.11
  def inject(*args, &block)
    raise ArgumentError, "too many arguments" if args.size > 2
    if Symbol === args[-1]
      sym = args[-1]
      block = ->(x,y){x.__send__(sym,y)}
      args.pop
    end
    if args.empty?
      flag = true  # no initial argument
      result = nil
    else
      flag = false
      result = args[0]
    end
    self.each{|*val|
      val = val.__svalue
      if flag
        # push first element as initial
        flag = false
        result = val
      else
        result = block.call(result, val)
      end
    }
    result
  end
  alias reduce inject

  ##
  # Alias for collect
  #
  # ISO 15.3.2.2.12
  alias map collect

  ##
  # Return the maximum value of all elements
  # yield by +each+. If no block is given <=>
  # will be invoked to define this value. If
  # a block is given it will be used instead.
  #
  # ISO 15.3.2.2.13
  def max(&block)
    flag = true  # 1st element?
    result = nil
    self.each{|*val|
      val = val.__svalue
      if flag
        # 1st element
        result = val
        flag = false
      else
        if block
          result = val if block.call(val, result) > 0
        else
          result = val if (val <=> result) > 0
        end
      end
    }
    result
  end

  ##
  # Return the minimum value of all elements
  # yield by +each+. If no block is given <=>
  # will be invoked to define this value. If
  # a block is given it will be used instead.
  #
  # ISO 15.3.2.2.14
  def min(&block)
    flag = true  # 1st element?
    result = nil
    self.each{|*val|
      val = val.__svalue
      if flag
        # 1st element
        result = val
        flag = false
      else
        if block
          result = val if block.call(val, result) < 0
        else
          result = val if (val <=> result) < 0
        end
      end
    }
    result
  end

  ##
  # Alias for include?
  #
  # ISO 15.3.2.2.15
  alias member? include?

  ##
  # Call the given block for each element
  # which is yield by +each+. Return an
  # array which contains two arrays. The
  # first array contains all elements
  # whose block value was true. The second
  # array contains all elements whose
  # block value was false.
  #
  # ISO 15.3.2.2.16
  def partition(&block)
    return to_enum :partition unless block

    ary_T = []
    ary_F = []
    self.each{|*val|
      if block.call(*val)
        ary_T.push(val.__svalue)
      else
        ary_F.push(val.__svalue)
      end
    }
    [ary_T, ary_F]
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Return an
  # array which contains only the elements
  # whose block value was false.
  #
  # ISO 15.3.2.2.17
  def reject(&block)
    return to_enum :reject unless block

    ary = []
    self.each{|*val|
      ary.push(val.__svalue) unless block.call(*val)
    }
    ary
  end

  ##
  # Alias for find_all.
  #
  # ISO 15.3.2.2.18
  alias select find_all

  ##
  # Return a sorted array of all elements
  # which are yield by +each+. If no block
  # is given <=> will be invoked on each
  # element to define the order. Otherwise
  # the given block will be used for
  # sorting.
  #
  # ISO 15.3.2.2.19
  def sort(&block)
    self.map{|*val| val.__svalue}.sort(&block)
  end

  ##
  # Alias for entries.
  #
  # ISO 15.3.2.2.20
  alias to_a entries

  # redefine #hash 15.3.1.3.15
  def hash
    h = 12347
    i = 0
    self.each do |e|
      h = __update_hash(h, i, e.hash)
      i += 1
    end
    h
  end
end

##
# Array
#
# ISO 15.2.12
class Array
  ##
  # call-seq:
  #   array.each {|element| ... } -> self
  #   array.each -> Enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the respective element.
  #
  # ISO 15.2.12.5.10
  def each(&block)
    return to_enum :each unless block

    idx = 0
    while idx < length
      block.call(self[idx])
      idx += 1
    end
    self
  end

  ##
  # call-seq:
  #   array.each_index {|index| ... } -> self
  #   array.each_index -> Enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the index of the respective element.
  #
  # ISO 15.2.12.5.11
  def each_index(&block)
    return to_enum :each_index unless block

    idx = 0
    while idx < length
      block.call(idx)
      idx += 1
    end
    self
  end

  ##
  # call-seq:
  #   array.collect! {|element| ... } -> self
  #   array.collect! -> new_enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the respective element. Each element will
  # be replaced by the resulting values.
  #
  # ISO 15.2.12.5.7
  def collect!(&block)
    return to_enum :collect! unless block

    idx = 0
    len = size
    while idx < len
      self[idx] = block.call self[idx]
      idx += 1
    end
    self
  end

  ##
  # call-seq:
  #   array.map! {|element| ... } -> self
  #   array.map! -> new_enumerator
  #
  # Alias for collect!
  #
  # ISO 15.2.12.5.20
  alias map! collect!

  ##
  # Private method for Array creation.
  #
  # ISO 15.2.12.5.15
  def initialize(size=0, obj=nil, &block)
    if size.is_a?(Array) && obj==nil && block == nil
      self.replace(size)
      return self
    end
    size = size.__to_int
    raise ArgumentError, "negative array size" if size < 0

    self.clear
    if size > 0
      self[size - 1] = nil # allocate

      idx = 0
      while idx < size
        self[idx] = (block)? block.call(idx): obj
        idx += 1
      end
    end

    self
  end

  ##
  # call-seq:
  #   array == other   -> true or false
  #
  #  Equality---Two arrays are equal if they contain the same number
  #  of elements and if each element is equal to (according to
  #  Object.==) the corresponding element in the other array.
  #
  def ==(other)
    other = self.__ary_eq(other)
    return false if other == false
    return true  if other == true
    len = self.size
    i = 0
    while i < len
      return false if self[i] != other[i]
      i += 1
    end
    return true
  end

  ##
  # call-seq:
  #   array.eql? other_array -> true or false
  #
  #  Returns <code>true</code> if +self+ and _other_ are the same object,
  #  or are both arrays with the same content.
  #
  def eql?(other)
    other = self.__ary_eq(other)
    return false if other == false
    return true  if other == true
    len = self.size
    i = 0
    while i < len
      return false unless self[i].eql?(other[i])
      i += 1
    end
    return true
  end

  ##
  # call-seq:
  #   array <=> other_array -> -1, 0, or 1
  #
  #  Comparison---Returns an integer (-1, 0, or +1)
  #  if this array is less than, equal to, or greater than <i>other_ary</i>.
  #  Each object in each array is compared (using <=>). If any value isn't
  #  equal, then that inequality is the return value. If all the
  #  values found are equal, then the return is based on a
  #  comparison of the array lengths. Thus, two arrays are
  #  "equal" according to <code>Array#<=></code> if and only if they have
  #  the same length and the value of each element is equal to the
  #  value of the corresponding element in the other array.
  #
  def <=>(other)
    other = self.__ary_cmp(other)
    return 0 if 0 == other
    return nil if nil == other

    len = self.size
    n = other.size
    len = n if len > n
    i = 0
    begin
      while i < len
        n = (self[i] <=> other[i])
        return n if n.nil? || n != 0
        i += 1
      end
    rescue NoMethodError
      return nil
    end
    len = self.size - other.size
    if len == 0
      0
    elsif len > 0
      1
    else
      -1
    end
  end

  ##
  # call-seq:
  #   array.delete(obj) -> deleted_object
  #   array.delete(obj) {|nosuch| ... } -> deleted_object or block_return
  #
  # Delete element with index +key+
  def delete(key, &block)
    while i = self.index(key)
      self.delete_at(i)
      ret = key
    end
    return block.call if ret.nil? && block
    ret
  end

  ##
  # call-seq:
  #   array.sort! -> self
  #   array.sort! {|a, b| ... } -> self
  #
  # Sort all elements and replace +self+ with these
  # elements.
  def sort!(&block)
    stack = [ [ 0, self.size - 1 ] ]
    until stack.empty?
      left, mid, right = stack.pop
      if right == nil
        right = mid
        # sort self[left..right]
        if left < right
          if left + 1 == right
            lval = self[left]
            rval = self[right]
            cmp = if block then block.call(lval,rval) else lval <=> rval end
            if cmp.nil?
              raise ArgumentError, "comparison of #{lval.inspect} and #{rval.inspect} failed"
            end
            if cmp > 0
              self[left]  = rval
              self[right] = lval
            end
          else
            mid = ((left + right + 1) / 2).floor
            stack.push [ left, mid, right ]
            stack.push [ mid, right ]
            stack.push [ left, (mid - 1) ] if left < mid - 1
          end
        end
      else
        lary = self[left, mid - left]
        lsize = lary.size

        # The entity sharing between lary and self may cause a large memory
        # copy operation in the merge loop below. This harmless operation
        # cancels the sharing and provides a huge performance gain.
        lary[0] = lary[0]

        # merge
        lidx = 0
        ridx = mid
        (left..right).each { |i|
          if lidx >= lsize
            break
          elsif ridx > right
            self[i, lsize - lidx] = lary[lidx, lsize - lidx]
            break
          else
            lval = lary[lidx]
            rval = self[ridx]
            cmp = if block then block.call(lval,rval) else lval <=> rval end
            if cmp.nil?
              raise ArgumentError, "comparison of #{lval.inspect} and #{rval.inspect} failed"
            end
            if cmp <= 0
              self[i] = lval
              lidx += 1
            else
              self[i] = rval
              ridx += 1
            end
          end
        }
      end
    end
    self
  end

  ##
  # call-seq:
  #   array.sort -> new_array
  #   array.sort {|a, b| ... } -> new_array
  #
  # Returns a new Array whose elements are those from +self+, sorted.
  def sort(&block)
    self.dup.sort!(&block)
  end

  ##
  # call-seq:
  #   array.to_a -> self
  #
  # Returns self, no need to convert.
  def to_a
    self
  end
  alias entries to_a

  ##
  # Array is enumerable
  # ISO 15.2.12.3
  include Enumerable
end

##
# Integer
#
# ISO 15.2.8
##
class Integer
  ##
  # Calls the given block once for each Integer
  # from +self+ downto +num+.
  #
  # ISO 15.2.8.3.15
  def downto(num, &block)
    return to_enum(:downto, num) unless block

    i = self.to_i
    while i >= num
      block.call(i)
      i -= 1
    end
    self
  end


  ##
  # Calls the given block +self+ times.
  #
  # ISO 15.2.8.3.22
  def times(&block)
    return to_enum :times unless block

    i = 0
    while i < self
      block.call i
      i += 1
    end
    self
  end


  ##
  # Calls the given block once for each Integer
  # from +self+ upto +num+.
  #
  # ISO 15.2.8.3.27
  def upto(num, &block)
    return to_enum(:upto, num) unless block

    i = self.to_i
    while i <= num
      block.call(i)
      i += 1
    end
    self
  end
end

##
# Range
#
# ISO 15.2.14
class Range
  ##
  # Range is enumerable
  #
  # ISO 15.2.14.3
  include Enumerable

  ##
  # Calls the given block for each element of +self+
  # and pass the respective element.
  #
  # ISO 15.2.14.4.4
  def each(&block)
    return to_enum :each unless block

    val = self.begin
    last = self.end

    if val.kind_of?(Integer) && last.nil?
      i = val
      while true
        block.call(i)
        i += 1
      end
      return self
    end

    if val.kind_of?(String) && last.nil?
      if val.respond_to? :__upto_endless
        return val.__upto_endless(&block)
      else
        str_each = true
      end
    end

    if val.kind_of?(Integer) && last.kind_of?(Integer) # integers are special
      lim = last
      lim += 1 unless exclude_end?
      i = val
      while i < lim
        block.call(i)
        i += 1
      end
      return self
    end

    if val.kind_of?(String) && last.kind_of?(String) # strings are special
      if val.respond_to? :upto
        return val.upto(last, exclude_end?, &block)
      else
        str_each = true
      end
    end

    raise TypeError, "can't iterate" unless val.respond_to? :succ

    return self if (val <=> last) > 0

    while (val <=> last) < 0
      block.call(val)
      val = val.succ
      if str_each
        break if val.size > last.size
      end
    end

    block.call(val) if !exclude_end? && (val <=> last) == 0
    self
  end

  # redefine #hash 15.3.1.3.15
  def hash
    h = first.hash ^ last.hash
    h += 1 if self.exclude_end?
    h
  end

  ##
  # call-seq:
  #    rng.to_a                   -> array
  #    rng.entries                -> array
  #
  # Returns an array containing the items in the range.
  #
  #   (1..7).to_a  #=> [1, 2, 3, 4, 5, 6, 7]
  #   (1..).to_a   #=> RangeError: cannot convert endless range to an array
  def to_a
    a = __num_to_a
    return a if a
    super
  end
  alias entries to_a
end

class Symbol
  def to_proc
    mid = self
    ->(obj,*args,**opts,&block) do
      obj.__send__(mid, *args, **opts, &block)
    end
  end
end

##
# Comparable
#
# ISO 15.3.3
module Comparable

  ##
  # call-seq:
  #   obj < other    -> true or false
  #
  # Return true if +self+ is less
  # than +other+. Otherwise return
  # false.
  #
  # ISO 15.3.3.2.1
  def < other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp < 0
  end

  ##
  # call-seq:
  #   obj <= other   -> true or false
  #
  # Return true if +self+ is less
  # than or equal to +other+.
  # Otherwise return false.
  #
  # ISO 15.3.3.2.2
  def <= other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp <= 0
  end

  ##
  # call-seq:
  #   obj == other   -> true or false
  #
  # Return true if +self+ is equal
  # to +other+. Otherwise return
  # false.
  #
  # ISO 15.3.3.2.3
  def == other
    cmp = self <=> other
    cmp.equal?(0)
  end

  ##
  # call-seq:
  #   obj > other    -> true or false
  #
  # Return true if +self+ is greater
  # than +other+. Otherwise return
  # false.
  #
  # ISO 15.3.3.2.4
  def > other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp > 0
  end

  ##
  # call-seq:
  #   obj >= other   -> true or false
  #
  # Return true if +self+ is greater
  # than or equal to +other+.
  # Otherwise return false.
  #
  # ISO 15.3.3.2.5
  def >= other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp >= 0
  end

  ##
  # call-seq:
  #   obj.between?(min,max) -> true or false
  #
  # Return true if +self+ is greater
  # than or equal to +min+ and
  # less than or equal to +max+.
  # Otherwise return false.
  #
  # ISO 15.3.3.2.6
  def between?(min, max)
    self >= min and self <= max
  end
end

##
# String
#
# ISO 15.2.10
class String
  # ISO 15.2.10.3
  include Comparable

  ##
  # Calls the given block for each line
  # and pass the respective line.
  #
  # ISO 15.2.10.5.15
  def each_line(separator = "\n", &block)
    return to_enum(:each_line, separator) unless block

    if separator.nil?
      block.call(self)
      return self
    end
    raise TypeError unless separator.is_a?(String)

    paragraph_mode = false
    if separator.empty?
      paragraph_mode = true
      separator = "\n\n"
    end
    start = 0
    string = dup
    self_len = self.bytesize
    sep_len = separator.bytesize

    while (pointer = string.byteindex(separator, start))
      pointer += sep_len
      pointer += 1 while paragraph_mode && string.getbyte(pointer) == 10 # 10 == \n
      block.call(string.byteslice(start, pointer - start))
      start = pointer
    end
    return self if start == self_len

    block.call(string.byteslice(start, self_len - start))
    self
  end

  ##
  # Replace all matches of +pattern+ with +replacement+.
  # Call block (if given) for each match and replace
  # +pattern+ with the value of the block. Return the
  # final value.
  #
  # ISO 15.2.10.5.18
  def gsub(*args, &block)
    return to_enum(:gsub, *args) if args.length == 1 && !block
    raise ArgumentError, "wrong number of arguments (given #{args.length}, expected 1..2)" unless (1..2).include?(args.length)

    pattern, replace = *args
    plen = pattern.length
    if args.length == 2 && block
      block = nil
    end
    offset = 0
    result = []
    while found = self.byteindex(pattern, offset)
      result << self.byteslice(offset, found - offset)
      offset = found + plen
      result << if block
        block.call(pattern).to_s
      else
        self.__sub_replace(replace, pattern, found)
      end
      if plen == 0
        result << self.byteslice(offset, 1)
        offset += 1
      end
    end
    result << self.byteslice(offset..-1) if offset < length
    result.join
  end

  ##
  # Replace all matches of +pattern+ with +replacement+.
  # Call block (if given) for each match and replace
  # +pattern+ with the value of the block. Modify
  # +self+ with the final value.
  #
  # ISO 15.2.10.5.19
  def gsub!(*args, &block)
    raise FrozenError, "can't modify frozen String" if frozen?
    return to_enum(:gsub!, *args) if args.length == 1 && !block
    str = self.gsub(*args, &block)
    return nil unless self.index(args[0])
    self.replace(str)
  end

#  ##
#  # Calls the given block for each match of +pattern+
#  # If no block is given return an array with all
#  # matches of +pattern+.
#  #
#  # ISO 15.2.10.5.32
#  def scan(pattern, &block)
#    # TODO: String#scan is not implemented yet
#  end

  ##
  # Replace only the first match of +pattern+ with
  # +replacement+. Call block (if given) for each
  # match and replace +pattern+ with the value of the
  # block. Return the final value.
  #
  # ISO 15.2.10.5.36
  def sub(*args, &block)
    unless (1..2).include?(args.length)
      raise ArgumentError, "wrong number of arguments (given #{args.length}, expected 2)"
    end

    pattern, replace = *args
    if args.length == 2 && block
      block = nil
    end
    result = []
    found = self.index(pattern)
    return self.dup unless found
    result << self.byteslice(0, found)
    offset = found + pattern.length
    result << if block
      block.call(pattern).to_s
    else
      self.__sub_replace(replace, pattern, found)
    end
    result << self.byteslice(offset..-1) if offset < length
    result.join
  end

  ##
  # Replace only the first match of +pattern+ with
  # +replacement+. Call block (if given) for each
  # match and replace +pattern+ with the value of the
  # block. Modify +self+ with the final value.
  #
  # ISO 15.2.10.5.37
  def sub!(*args, &block)
    raise FrozenError, "can't modify frozen String" if frozen?
    str = self.sub(*args, &block)
    return nil unless self.index(args[0])
    self.replace(str)
  end

  ##
  # Call the given block for each byte of +self+.
  def each_byte(&block)
    return to_enum(:each_byte, &block) unless block
    pos = 0
    while pos < bytesize
      block.call(getbyte(pos))
      pos += 1
    end
    self
  end

  # those two methods requires Regexp that is optional in mruby
  ##
  # ISO 15.2.10.5.3
  #def =~(re)
  # re =~ self
  #end

  ##
  # ISO 15.2.10.5.27
  #def match(re, &block)
  #  re.match(self, &block)
  #end
end
